using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Calculation;

/// <summary>
/// Performs sprinkler hydraulic calculations per NFPA 13.
/// Calculates Q = K × √P for each sprinkler head, validates spacing,
/// coverage area, and density against hazard classification.
/// </summary>
[Skill("sprinkler_hydraulic_calc",
    "Calculate sprinkler hydraulic performance per NFPA 13. " +
    "Validates K-factor flow (Q = K√P), head spacing, coverage area, " +
    "and density against hazard classification (Light/OH1/OH2/EH).")]
[SkillParameter("hazard_class", "string",
    "Hazard classification. Default: 'light'.",
    isRequired: false, allowedValues: new[] { "light", "oh1", "oh2", "extra" })]
[SkillParameter("k_factor", "number",
    "K-factor of sprinkler heads (default: 80 for K80)", isRequired: false)]
[SkillParameter("min_pressure_bar", "number",
    "Minimum required pressure at sprinkler head in bar. Default: 0.5.", isRequired: false)]
[SkillParameter("level", "string", "Filter by level name (optional)", isRequired: false)]
public class SprinklerHydraulicSkill : CalculationSkillBase
{
    protected override string SkillName => "sprinkler_hydraulic_calc";

    private static readonly Dictionary<string, HazardCriteria> HazardTable = new()
    {
        ["light"] = new(21, 4.6, 139, 4.1),
        ["oh1"] = new(12, 4.6, 139, 6.1),
        ["oh2"] = new(12, 4.6, 139, 8.2),
        ["extra"] = new(9, 3.7, 232, 12.2)
    };

    public override async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var hazardClass = GetParamString(parameters, context, "hazard_class", "light");
        var kFactor = GetParamDouble(parameters, context, "k_factor", 80);
        var minPressure = GetParamDouble(parameters, context, "min_pressure_bar", 0.5);
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();

        if (!HazardTable.TryGetValue(hazardClass, out var criteria))
            criteria = HazardTable["light"];

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var sprinklers = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Sprinklers)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            if (!string.IsNullOrWhiteSpace(levelFilter))
                sprinklers = sprinklers.Where(s =>
                    (document.GetElement(s.LevelId) as Level)?.Name?
                    .Contains(levelFilter, StringComparison.OrdinalIgnoreCase) == true).ToList();

            var issues = new List<object>();
            var headData = new List<object>();

            foreach (var sprinkler in sprinklers)
            {
                var pressureParam = sprinkler.LookupParameter("Pressure") ??
                    sprinkler.LookupParameter("Pressure Drop") ??
                    sprinkler.LookupParameter("Static Pressure");
                var pressurePa = pressureParam?.AsDouble() ?? 0;
                var pressureBar = pressurePa / 100000.0;

                // Q = K × √P (L/min when P in bar)
                var flowLpm = kFactor * Math.Sqrt(Math.Max(pressureBar, minPressure));
                var flowLps = flowLpm / 60.0;

                var coverageArea = criteria.CoveragePerHeadM2;
                var density = flowLpm / coverageArea;

                var loc = (sprinkler.Location as LocationPoint)?.Point;
                var level = (document.GetElement(sprinkler.LevelId) as Level)?.Name ?? "N/A";

                var headIssues = new List<string>();
                if (pressureBar < minPressure && pressurePa > 0)
                    headIssues.Add($"Low pressure: {pressureBar:F2} bar < {minPressure} bar");
                if (density < criteria.MinDensityMmPerMin)
                    headIssues.Add($"Low density: {density:F1} mm/min < {criteria.MinDensityMmPerMin}");

                var entry = new
                {
                    id = sprinkler.Id.Value,
                    level,
                    familyName = sprinkler.Symbol?.FamilyName ?? "N/A",
                    pressureBar = Math.Round(pressureBar, 3),
                    flowLpm = Math.Round(flowLpm, 1),
                    flowLps = Math.Round(flowLps, 3),
                    densityMmPerMin = Math.Round(density, 1),
                    status = headIssues.Count > 0 ? "ISSUE" : "OK",
                    issues = headIssues
                };

                headData.Add(entry);
                if (headIssues.Count > 0) issues.Add(entry);
            }

            var totalFlow = headData.Sum(h => (double)((dynamic)h).flowLps);
            var minDesignHeads = (int)Math.Ceiling(criteria.DesignAreaM2 / criteria.CoveragePerHeadM2);

            return new
            {
                hazardClassification = hazardClass.ToUpper(),
                criteria = new
                {
                    coveragePerHeadM2 = criteria.CoveragePerHeadM2,
                    maxSpacingM = criteria.MaxSpacingM,
                    designAreaM2 = criteria.DesignAreaM2,
                    minDensityMmPerMin = criteria.MinDensityMmPerMin,
                    minDesignHeads,
                    kFactor,
                    minPressureBar = minPressure
                },
                totalHeads = headData.Count,
                issueCount = issues.Count,
                totalFlowLps = Math.Round(totalFlow, 1),
                totalFlowGPM = Math.Round(totalFlow * 15.85, 1),
                issues = issues.Take(20).ToList(),
                summary = headData.Take(20).ToList()
            };
        });

        var totalHeads = (int)((dynamic)result!).totalHeads;
        var issueCount = (int)((dynamic)result!).issueCount;
        var calcSummary = new CalcResultSummary { TotalItems = totalHeads, IssueCount = issueCount };
        var delta = ComputeDelta(context, calcSummary);
        SaveResultForDelta(context, calcSummary);

        var msg = "Sprinkler hydraulic calculation completed.";
        if (delta is not null) msg += $"\n{delta.Summary}";

        var followUps = new List<FollowUpSuggestion>();
        if (issueCount > 0)
            followUps.Add(new FollowUpSuggestion
            {
                SkillName = "calculate_pressure_drop",
                Reason = $"{issueCount} sprinkler issue(s) — check pipe pressure drop",
                PrefilledParams = { ["system_type"] = "pipe" }
            });

        msg = AppendFollowUps(msg, followUps);
        return OkPaginated(msg, result, totalHeads, Math.Min(totalHeads, 20), "heads");
    }

    private record HazardCriteria(
        double CoveragePerHeadM2,
        double MaxSpacingM,
        double DesignAreaM2,
        double MinDensityMmPerMin);
}
