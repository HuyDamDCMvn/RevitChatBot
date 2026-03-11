using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Check;

/// <summary>
/// Checks flow balance of MEP systems. Compares supply vs return/exhaust
/// airflow rates and flags imbalanced systems.
/// </summary>
[Skill("check_system_balance",
    "Check MEP system flow balance. Compares supply vs return/exhaust airflow or " +
    "water flow in systems to find imbalances exceeding the tolerance threshold. " +
    "Helps ensure proper system performance.")]
[SkillParameter("system_type", "string",
    "'hvac' or 'hydronic'. Default: 'hvac'.",
    isRequired: false, allowedValues: new[] { "hvac", "hydronic" })]
[SkillParameter("max_imbalance_percent", "number",
    "Maximum allowed flow imbalance in %. Default: 10.",
    isRequired: false)]
[SkillParameter("level", "string",
    "Filter by level name (optional).",
    isRequired: false)]
public class CheckSystemBalanceSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var systemType = parameters.GetValueOrDefault("system_type")?.ToString() ?? "hvac";
        var maxImbalance = ParseDouble(parameters.GetValueOrDefault("max_imbalance_percent"), 10);
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var systemFlows = new Dictionary<string, SystemFlowData>();

            if (systemType == "hvac")
                AnalyzeHvacBalance(document, levelFilter, systemFlows);
            else
                AnalyzeHydronicBalance(document, levelFilter, systemFlows);

            var results = systemFlows.Values
                .OrderByDescending(s => s.ImbalancePercent)
                .Select(s => new
                {
                    systemName = s.SystemName,
                    classification = s.Classification,
                    supplyFlow = Math.Round(s.SupplyFlow, 1),
                    returnFlow = Math.Round(s.ReturnFlow, 1),
                    imbalancePercent = Math.Round(s.ImbalancePercent, 1),
                    status = s.ImbalancePercent > maxImbalance ? "IMBALANCED" : "OK",
                    unit = systemType == "hvac" ? "L/s" : "L/s"
                })
                .ToList();

            return new
            {
                totalSystems = results.Count,
                imbalancedSystems = results.Count(r => r.status == "IMBALANCED"),
                maxAllowedImbalancePercent = maxImbalance,
                systems = results
            };
        });

        return SkillResult.Ok("System balance check completed.", result);
    }

    private static void AnalyzeHvacBalance(Document doc, string? levelFilter,
        Dictionary<string, SystemFlowData> flows)
    {
        var spaces = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_MEPSpaces)
            .WhereElementIsNotElementType()
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
            spaces = spaces.Where(s => GetLevelName(doc, s)
                .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var space in spaces)
        {
            var supplyAirflow = space.get_Parameter(BuiltInParameter.ROOM_DESIGN_SUPPLY_AIRFLOW_PARAM)?.AsDouble() ?? 0;
            var returnAirflow = space.get_Parameter(BuiltInParameter.ROOM_DESIGN_RETURN_AIRFLOW_PARAM)?.AsDouble() ?? 0;

            var supplyLps = supplyAirflow * 0.47195;
            var returnLps = returnAirflow * 0.47195;

            var spaceName = space.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "Unknown";
            var level = GetLevelName(doc, space);
            var key = $"{level} - Spaces";

            if (!flows.TryGetValue(key, out var data))
            {
                data = new SystemFlowData { SystemName = key, Classification = "HVAC Spaces" };
                flows[key] = data;
            }

            data.SupplyFlow += supplyLps;
            data.ReturnFlow += returnLps;
        }

        var ducts = new FilteredElementCollector(doc)
            .OfClass(typeof(Duct))
            .WhereElementIsNotElementType()
            .Cast<Duct>()
            .ToList();

        var systemGroups = ducts.GroupBy(d =>
            d.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "Unassigned");

        foreach (var group in systemGroups)
        {
            var classification = group.First()
                .get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "";

            if (!flows.TryGetValue(group.Key, out var data))
            {
                data = new SystemFlowData { SystemName = group.Key, Classification = classification };
                flows[group.Key] = data;
            }

            foreach (var duct in group)
            {
                var flow = duct.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM)?.AsDouble() ?? 0;
                var flowLps = flow * 0.47195;

                if (classification.Contains("Supply", StringComparison.OrdinalIgnoreCase))
                    data.SupplyFlow = Math.Max(data.SupplyFlow, flowLps);
                else if (classification.Contains("Return", StringComparison.OrdinalIgnoreCase) ||
                         classification.Contains("Exhaust", StringComparison.OrdinalIgnoreCase))
                    data.ReturnFlow = Math.Max(data.ReturnFlow, flowLps);
            }
        }
    }

    private static void AnalyzeHydronicBalance(Document doc, string? levelFilter,
        Dictionary<string, SystemFlowData> flows)
    {
        var pipes = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_PipeCurves)
            .WhereElementIsNotElementType()
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
            pipes = pipes.Where(p => GetLevelName(doc, p)
                .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        var systemGroups = pipes.GroupBy(p =>
            p.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "Unassigned");

        foreach (var group in systemGroups)
        {
            var classification = group.First()
                .get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "";

            var key = group.Key;
            if (!flows.TryGetValue(key, out var data))
            {
                data = new SystemFlowData { SystemName = key, Classification = classification };
                flows[key] = data;
            }

            foreach (var pipe in group)
            {
                var flow = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_FLOW_PARAM)?.AsDouble() ?? 0;
                var flowLps = flow * 0.47195;

                if (classification.Contains("Supply", StringComparison.OrdinalIgnoreCase))
                    data.SupplyFlow = Math.Max(data.SupplyFlow, flowLps);
                else if (classification.Contains("Return", StringComparison.OrdinalIgnoreCase))
                    data.ReturnFlow = Math.Max(data.ReturnFlow, flowLps);
            }
        }
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var lvlId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId() ?? elem.LevelId;
        if (lvlId is null || lvlId == ElementId.InvalidElementId) return "";
        return doc.GetElement(lvlId)?.Name ?? "";
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }

    private class SystemFlowData
    {
        public string SystemName { get; set; } = "";
        public string Classification { get; set; } = "";
        public double SupplyFlow { get; set; }
        public double ReturnFlow { get; set; }

        public double ImbalancePercent
        {
            get
            {
                var total = SupplyFlow + ReturnFlow;
                if (total < 0.01) return 0;
                return Math.Abs(SupplyFlow - ReturnFlow) / ((SupplyFlow + ReturnFlow) / 2) * 100;
            }
        }
    }
}
