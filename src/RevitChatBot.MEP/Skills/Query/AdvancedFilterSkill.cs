using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Query;

[Skill("advanced_filter",
    "Multi-criteria element filter inspired by DiRoots OneFilter. " +
    "Combines category, level, system, parameter value, and room/space filters " +
    "in a single query. Returns matching elements with option to select them in Revit.")]
[SkillParameter("category", "string",
    "Category: 'ducts', 'pipes', 'equipment', 'fittings', 'air_terminals', 'electrical', " +
    "'plumbing', 'cable_trays', 'conduits', 'sprinklers'.",
    isRequired: true)]
[SkillParameter("level", "string",
    "Level name to filter (e.g. 'Level 1'). Supports partial match.",
    isRequired: false)]
[SkillParameter("system_name", "string",
    "System name filter (e.g. 'Supply Air', 'Chilled Water'). Partial match.",
    isRequired: false)]
[SkillParameter("parameter_name", "string",
    "Parameter name to filter by (e.g. 'Size', 'Diameter', 'Mark').",
    isRequired: false)]
[SkillParameter("parameter_operator", "string",
    "Comparison: 'equals', 'contains', 'greater_than', 'less_than', 'not_equals'.",
    isRequired: false,
    allowedValues: new[] { "equals", "contains", "greater_than", "less_than", "not_equals" })]
[SkillParameter("parameter_value", "string",
    "Value to compare against. Required when parameter_name is specified.",
    isRequired: false)]
[SkillParameter("room_name", "string",
    "Filter by room/space name containing this text.",
    isRequired: false)]
[SkillParameter("select_in_revit", "string",
    "'true' to select matching elements in Revit's active view. Default 'false'.",
    isRequired: false)]
[SkillParameter("max_results", "integer",
    "Max elements to return details for. Default 50.",
    isRequired: false)]
public class AdvancedFilterSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ducts"] = BuiltInCategory.OST_DuctCurves,
        ["pipes"] = BuiltInCategory.OST_PipeCurves,
        ["equipment"] = BuiltInCategory.OST_MechanicalEquipment,
        ["fittings"] = BuiltInCategory.OST_DuctFitting,
        ["pipe_fittings"] = BuiltInCategory.OST_PipeFitting,
        ["air_terminals"] = BuiltInCategory.OST_DuctTerminal,
        ["electrical"] = BuiltInCategory.OST_ElectricalEquipment,
        ["plumbing"] = BuiltInCategory.OST_PlumbingFixtures,
        ["cable_trays"] = BuiltInCategory.OST_CableTray,
        ["conduits"] = BuiltInCategory.OST_Conduit,
        ["sprinklers"] = BuiltInCategory.OST_Sprinklers,
        ["flex_ducts"] = BuiltInCategory.OST_FlexDuctCurves,
        ["flex_pipes"] = BuiltInCategory.OST_FlexPipeCurves,
        ["duct_accessories"] = BuiltInCategory.OST_DuctAccessory,
        ["pipe_accessories"] = BuiltInCategory.OST_PipeAccessory,
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var categoryStr = parameters.GetValueOrDefault("category")?.ToString() ?? "";
        if (!CategoryMap.TryGetValue(categoryStr.Trim(), out var bic))
            return SkillResult.Fail($"Unknown category '{categoryStr}'. Supported: {string.Join(", ", CategoryMap.Keys)}");

        var level = parameters.GetValueOrDefault("level")?.ToString();
        var systemName = parameters.GetValueOrDefault("system_name")?.ToString();
        var paramName = parameters.GetValueOrDefault("parameter_name")?.ToString();
        var paramOp = parameters.GetValueOrDefault("parameter_operator")?.ToString() ?? "equals";
        var paramValue = parameters.GetValueOrDefault("parameter_value")?.ToString();
        var roomName = parameters.GetValueOrDefault("room_name")?.ToString();
        var selectInRevit = parameters.GetValueOrDefault("select_in_revit")?.ToString()?.ToLower() == "true";
        var maxResults = 50;
        if (parameters.TryGetValue("max_results", out var mr) && mr is not null)
            int.TryParse(mr.ToString(), out maxResults);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var totalBeforeFilter = new FluentCollector(document)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .Count();

            var collector = new FluentCollector(document)
                .OfCategory(bic)
                .WhereElementIsNotElementType();

            if (!string.IsNullOrWhiteSpace(level))
                collector.OnLevel(level);

            if (!string.IsNullOrWhiteSpace(systemName))
                collector.InSystem(systemName);

            if (!string.IsNullOrWhiteSpace(paramName) && !string.IsNullOrWhiteSpace(paramValue))
                collector.WhereParameter(paramName, paramOp, paramValue);

            if (!string.IsNullOrWhiteSpace(roomName))
                collector.InRoom(roomName);

            var elements = collector.ToList();
            var matchedIds = elements.Select(e => e.Id).ToList();

            var selectedInView = false;
            if (selectInRevit && matchedIds.Count > 0)
            {
                try
                {
                    var view = document.ActiveView;
                    if (view is not null)
                    {
                        using var tx = new Transaction(document, "Isolate filtered elements");
                        tx.Start();
                        view.IsolateElementsTemporary(matchedIds);
                        tx.Commit();
                        selectedInView = true;
                    }
                }
                catch { }
            }

            var summaries = elements.Take(maxResults).Select(e => new
            {
                id = e.Id.Value,
                name = e.Name,
                type = e.GetTypeName(document),
                level = e.GetLevelName(document),
                system = e.GetSystemName(),
                size = e.GetSize(),
            }).ToList();

            return new
            {
                totalInCategory = totalBeforeFilter,
                matchedCount = elements.Count,
                returnedCount = summaries.Count,
                selectedInRevit = selectedInView,
                filters = new
                {
                    category = categoryStr,
                    level,
                    systemName,
                    parameter = paramName is not null ? $"{paramName} {paramOp} {paramValue}" : null,
                    roomName
                },
                elements = summaries
            };
        });

        dynamic res = result!;
        return SkillResult.Ok(
            $"Found {res.matchedCount} elements matching filters (from {res.totalInCategory} total in category).",
            result);
    }
}
