using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Calculation;

/// <summary>
/// Calculates pressure drop across duct/pipe segments using Revit's
/// built-in pressure loss parameters and summarizes by system.
/// </summary>
[Skill("calculate_pressure_drop",
    "Calculate pressure drop for duct or pipe systems. Summarizes friction loss, " +
    "fitting loss, and total pressure drop per system and per level. " +
    "Identifies systems exceeding the allowed max pressure drop.")]
[SkillParameter("system_type", "string",
    "Type of system to analyze: 'duct' or 'pipe'. Default: 'duct'.",
    isRequired: false, allowedValues: new[] { "duct", "pipe" })]
[SkillParameter("system_name", "string",
    "Filter by system name (optional). Leave empty for all systems.",
    isRequired: false)]
[SkillParameter("max_pressure_drop_pa", "number",
    "Maximum allowed pressure drop in Pa. Violations above this are flagged. Default: 1500.",
    isRequired: false)]
public class PressureDropSkill : CalculationSkillBase
{
    protected override string SkillName => "calculate_pressure_drop";

    public override async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var systemType = GetParamString(parameters, context, "system_type", "duct");
        var systemNameFilter = parameters.GetValueOrDefault("system_name")?.ToString();
        var maxPdPa = GetParamDouble(parameters, context, "max_pressure_drop_pa", 1500);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            bool isDuct = systemType.Equals("duct", StringComparison.OrdinalIgnoreCase);

            var elements = isDuct
                ? new FilteredElementCollector(document)
                    .OfClass(typeof(Duct))
                    .WhereElementIsNotElementType()
                    .ToList()
                : new FilteredElementCollector(document)
                    .OfClass(typeof(Pipe))
                    .WhereElementIsNotElementType()
                    .ToList();

            var systemData = new Dictionary<string, SystemPdData>();

            foreach (var elem in elements)
            {
                var sysName = GetSystemName(elem);
                if (!string.IsNullOrWhiteSpace(systemNameFilter) &&
                    !sysName.Contains(systemNameFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!systemData.TryGetValue(sysName, out var data))
                {
                    data = new SystemPdData { SystemName = sysName };
                    systemData[sysName] = data;
                }

                var frictionPd = isDuct
                    ? GetParamValue(elem, BuiltInParameter.RBS_DUCT_PRESSURE_DROP)
                    : (elem.LookupParameter("Pressure Drop")?.AsDouble() ?? 0);

                var lengthFt = GetParamValue(elem, BuiltInParameter.CURVE_ELEM_LENGTH);
                var lengthM = lengthFt * 0.3048;

                data.TotalFrictionPa += frictionPd;
                data.TotalLengthM += lengthM;
                data.SegmentCount++;
            }

            var fittings = isDuct
                ? new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_DuctFitting)
                    .WhereElementIsNotElementType()
                    .ToList()
                : new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_PipeFitting)
                    .WhereElementIsNotElementType()
                    .ToList();

            foreach (var fit in fittings)
            {
                var sysName = GetSystemName(fit);
                if (!string.IsNullOrWhiteSpace(systemNameFilter) &&
                    !sysName.Contains(systemNameFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!systemData.TryGetValue(sysName, out var data))
                {
                    data = new SystemPdData { SystemName = sysName };
                    systemData[sysName] = data;
                }

                var fittingPd = isDuct
                    ? GetParamValue(fit, BuiltInParameter.RBS_DUCT_PRESSURE_DROP)
                    : (fit.LookupParameter("Pressure Drop")?.AsDouble() ?? 0);

                data.TotalFittingPa += fittingPd;
                data.FittingCount++;
            }

            var results = systemData.Values
                .OrderByDescending(s => s.TotalPressureDropPa)
                .Select(s => new
                {
                    system = s.SystemName,
                    segments = s.SegmentCount,
                    fittings = s.FittingCount,
                    totalLengthM = Math.Round(s.TotalLengthM, 1),
                    frictionPa = Math.Round(s.TotalFrictionPa, 1),
                    fittingPa = Math.Round(s.TotalFittingPa, 1),
                    totalPressureDropPa = Math.Round(s.TotalPressureDropPa, 1),
                    exceedsMax = s.TotalPressureDropPa > maxPdPa
                })
                .ToList();

            return new
            {
                systemType,
                totalSystems = results.Count,
                violations = results.Count(r => r.exceedsMax),
                maxAllowedPa = maxPdPa,
                systems = results
            };
        });

        var violations = (int)((dynamic)result!).violations;
        var totalSystems = (int)((dynamic)result!).totalSystems;
        var summary = new CalcResultSummary
        {
            TotalItems = totalSystems,
            IssueCount = violations
        };
        var delta = ComputeDelta(context, summary);
        SaveResultForDelta(context, summary);

        var msg = "Pressure drop analysis completed.";
        if (delta is not null) msg += $"\n{delta.Summary}";

        var followUps = new List<FollowUpSuggestion>();
        if (violations > 0)
            followUps.Add(new FollowUpSuggestion
            {
                SkillName = systemType == "duct" ? "duct_sizing_analysis" : "pipe_sizing_analysis",
                Reason = $"{violations} system(s) exceed max pressure drop — check sizing"
            });

        msg = AppendFollowUps(msg, followUps);
        return SkillResult.Ok(msg, result);
    }

    private static string GetSystemName(Element elem)
    {
        var sys = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString();
        return string.IsNullOrWhiteSpace(sys) ? "Unassigned" : sys;
    }

    private static double GetParamValue(Element elem, BuiltInParameter bip)
    {
        var p = elem.get_Parameter(bip);
        return p?.AsDouble() ?? 0;
    }

    private class SystemPdData
    {
        public string SystemName { get; set; } = "";
        public int SegmentCount { get; set; }
        public int FittingCount { get; set; }
        public double TotalLengthM { get; set; }
        public double TotalFrictionPa { get; set; }
        public double TotalFittingPa { get; set; }
        public double TotalPressureDropPa => TotalFrictionPa + TotalFittingPa;
    }
}
