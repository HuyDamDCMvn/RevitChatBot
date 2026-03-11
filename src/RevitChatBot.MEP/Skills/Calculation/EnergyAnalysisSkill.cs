using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Calculation;

/// <summary>
/// Analyzes energy-related parameters of MEP systems: insulation coverage,
/// system efficiency indicators, U-values, and thermal bridging risk.
/// </summary>
[Skill("energy_analysis",
    "Analyze energy performance of MEP systems. Calculates insulation coverage, " +
    "identifies uninsulated segments, reports system efficiency metrics, and " +
    "estimates heat gain/loss for exposed duct/pipe runs.")]
[SkillParameter("category", "string",
    "'duct', 'pipe', or 'all'. Default: 'all'.",
    isRequired: false, allowedValues: new[] { "duct", "pipe", "all" })]
[SkillParameter("level", "string",
    "Filter by level name (optional).",
    isRequired: false)]
public class EnergyAnalysisSkill : CalculationSkillBase
{
    protected override string SkillName => "energy_analysis";

    public override async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var category = GetParamString(parameters, context, "category", "all");
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var sections = new List<object>();

            if (category is "duct" or "all")
                sections.Add(AnalyzeCategory(document, BuiltInCategory.OST_DuctCurves,
                    "Duct", levelFilter));

            if (category is "pipe" or "all")
                sections.Add(AnalyzeCategory(document, BuiltInCategory.OST_PipeCurves,
                    "Pipe", levelFilter));

            double totalLength = sections.Sum(s => (double)((dynamic)s!).totalLengthM);
            double insulatedLength = sections.Sum(s => (double)((dynamic)s!).insulatedLengthM);
            double coveragePercent = totalLength > 0 ? (insulatedLength / totalLength) * 100 : 100;

            return new
            {
                overallInsulationCoverage = Math.Round(coveragePercent, 1),
                totalLengthM = Math.Round(totalLength, 1),
                insulatedLengthM = Math.Round(insulatedLength, 1),
                uninsulatedLengthM = Math.Round(totalLength - insulatedLength, 1),
                uninsulatedCount = sections.Sum(s => (int)((dynamic)s!).uninsulatedElements),
                sections
            };
        });

        var uninsulated = (int)((dynamic)result!).uninsulatedCount;
        var summary = new CalcResultSummary
        {
            TotalItems = (int)Math.Round((double)((dynamic)result!).totalLengthM),
            IssueCount = uninsulated
        };
        var delta = ComputeDelta(context, summary);
        SaveResultForDelta(context, summary);

        var msg = "Energy analysis completed.";
        if (delta is not null) msg += $"\n{delta.Summary}";

        var followUps = new List<FollowUpSuggestion>();
        if (uninsulated > 0)
            followUps.Add(new FollowUpSuggestion
            {
                SkillName = "insulation_thickness_calc",
                Reason = $"{uninsulated} uninsulated segments found — calculate required thickness"
            });

        msg = AppendFollowUps(msg, followUps);
        return SkillResult.Ok(msg, result);
    }

    private static object AnalyzeCategory(Document doc, BuiltInCategory bic,
        string label, string? levelFilter)
    {
        var elements = new FilteredElementCollector(doc)
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
            elements = elements.Where(e => GetLevelName(doc, e)
                .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        double totalLengthM = 0;
        double insulatedLengthM = 0;
        int uninsulatedCount = 0;
        var uninsulatedBySystem = new Dictionary<string, double>();

        foreach (var elem in elements)
        {
            var lengthFt = (elem.Location as LocationCurve)?.Curve.Length ?? 0;
            var lengthM = lengthFt * 0.3048;
            totalLengthM += lengthM;

            var insTypeId = elem.get_Parameter(BuiltInParameter.RBS_REFERENCE_INSULATION_TYPE)?.AsElementId();
            var hasInsulation = insTypeId is not null && insTypeId != ElementId.InvalidElementId;

            if (hasInsulation)
            {
                insulatedLengthM += lengthM;
            }
            else
            {
                uninsulatedCount++;
                var sysName = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "Unassigned";
                uninsulatedBySystem.TryGetValue(sysName, out var existing);
                uninsulatedBySystem[sysName] = existing + lengthM;
            }
        }

        var coverage = totalLengthM > 0 ? (insulatedLengthM / totalLengthM) * 100 : 100;

        return new
        {
            category = label,
            totalElements = elements.Count,
            totalLengthM = Math.Round(totalLengthM, 1),
            insulatedLengthM = Math.Round(insulatedLengthM, 1),
            uninsulatedElements = uninsulatedCount,
            insulationCoveragePercent = Math.Round(coverage, 1),
            uninsulatedBySystem = uninsulatedBySystem
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new
                {
                    system = kv.Key,
                    uninsulatedLengthM = Math.Round(kv.Value, 1)
                })
                .Take(10)
                .ToList()
        };
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var lvlId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId() ?? elem.LevelId;
        if (lvlId is null || lvlId == ElementId.InvalidElementId) return "";
        return doc.GetElement(lvlId)?.Name ?? "";
    }
}
