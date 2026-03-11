using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using RevitChatBot.Core.Skills;
using RevitChatBot.MEP.Skills.Calculation;

namespace RevitChatBot.MEP.Skills.HVAC;

/// <summary>
/// Analyzes duct sizing: finds undersized/oversized ducts based on velocity limits.
/// </summary>
[Skill("duct_sizing_analysis",
    "Analyze duct sizing in the model. Check for undersized or oversized ducts based on " +
    "velocity limits. Returns size, airflow, velocity, and sizing assessment per duct.")]
[SkillParameter("system_name", "string", "Filter by system name (optional)", isRequired: false)]
[SkillParameter("max_velocity_fpm", "number",
    "Maximum allowed velocity in FPM (default: 2000 for main ducts)", isRequired: false)]
[SkillParameter("min_velocity_fpm", "number",
    "Minimum recommended velocity in FPM (default: 600)", isRequired: false)]
public class DuctSizingSkill : CalculationSkillBase
{
    protected override string SkillName => "duct_sizing_analysis";

    public override async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var systemName = parameters.GetValueOrDefault("system_name")?.ToString();
        var maxVel = GetParamDouble(parameters, context, "max_velocity_fpm", 2000);
        var minVel = GetParamDouble(parameters, context, "min_velocity_fpm", 600);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var ducts = new FilteredElementCollector(document)
                .OfClass(typeof(Duct))
                .WhereElementIsNotElementType()
                .Cast<Duct>()
                .ToList();

            if (systemName is not null)
                ducts = ducts.Where(d =>
                    d.MEPSystem?.Name?.Contains(systemName, StringComparison.OrdinalIgnoreCase) == true).ToList();

            var analysis = ducts.Select(d =>
            {
                var size = d.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";
                var velocity = d.get_Parameter(BuiltInParameter.RBS_VELOCITY)?.AsDouble() ?? 0;
                var flow = d.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM)?.AsDouble() ?? 0;
                var length = d.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0;

                var status = velocity > maxVel ? "OVERSIZED_VELOCITY"
                    : velocity < minVel && velocity > 0 ? "LOW_VELOCITY"
                    : "OK";

                return new
                {
                    id = d.Id.Value,
                    systemName = d.MEPSystem?.Name ?? "Unassigned",
                    size,
                    flowCFM = Math.Round(flow, 1),
                    velocityFPM = Math.Round(velocity, 1),
                    velocityMps = Math.Round(velocity * 0.00508, 2),
                    lengthFt = Math.Round(length, 2),
                    status
                };
            }).ToList();

            var issues = analysis.Where(a => a.status != "OK").ToList();

            return new
            {
                totalDucts = analysis.Count,
                issueCount = issues.Count,
                oversizedCount = issues.Count(i => i.status == "OVERSIZED_VELOCITY"),
                lowVelocityCount = issues.Count(i => i.status == "LOW_VELOCITY"),
                velocityLimits = new { maxFPM = maxVel, minFPM = minVel },
                issues,
                summary = analysis.Take(20).ToList()
            };
        });

        var totalDucts = (int)((dynamic)result!).totalDucts;
        var issueCount = (int)((dynamic)result!).issueCount;
        var calcSummary = new CalcResultSummary { TotalItems = totalDucts, IssueCount = issueCount };
        var delta = ComputeDelta(context, calcSummary);
        SaveResultForDelta(context, calcSummary);

        var msg = "Duct sizing analysis completed.";
        if (delta is not null) msg += $"\n{delta.Summary}";

        var followUps = new List<FollowUpSuggestion>();
        if (issueCount > 0)
            followUps.Add(new FollowUpSuggestion
            {
                SkillName = "calculate_pressure_drop",
                Reason = $"{issueCount} velocity issue(s) found — check pressure drop impact",
                PrefilledParams = { ["system_type"] = "duct" }
            });

        msg = AppendFollowUps(msg, followUps);
        return OkPaginated(msg, result, totalDucts,
            Math.Min(totalDucts, 20), "ducts");
    }
}
