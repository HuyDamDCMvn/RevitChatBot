using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Plumbing;

/// <summary>
/// Analyzes sanitary/storm drainage systems: pipe slopes, fixture unit counts, and compliance.
/// </summary>
[Skill("drainage_analysis",
    "Analyze drainage (sanitary/storm) systems in the model. Check pipe slopes, " +
    "diameters, and provide drainage system summary.")]
[SkillParameter("system_type", "string",
    "Type of drainage system to analyze", isRequired: false,
    allowedValues: new[] { "sanitary", "storm", "all" })]
[SkillParameter("min_slope", "number",
    "Minimum required slope in inch/ft (default: 0.125 = 1/8 inch per foot)", isRequired: false)]
public class DrainageCalculationSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var sysType = parameters.GetValueOrDefault("system_type")?.ToString() ?? "all";
        var minSlope = ParseDouble(parameters.GetValueOrDefault("min_slope"), 0.125);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var pipes = new FilteredElementCollector(document)
                .OfClass(typeof(Pipe))
                .WhereElementIsNotElementType()
                .Cast<Pipe>()
                .Where(p =>
                {
                    var sys = p.MEPSystem as PipingSystem;
                    if (sys is null) return false;
                    return sysType switch
                    {
                        "sanitary" => sys.SystemType == PipeSystemType.Sanitary,
                        "storm" => sys.SystemType == PipeSystemType.OtherPipe,
                        _ => sys.SystemType == PipeSystemType.Sanitary || sys.SystemType == PipeSystemType.OtherPipe
                    };
                }).ToList();

            var analysis = pipes.Select(p =>
            {
                var slope = p.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE)?.AsDouble() ?? 0;
                var diameter = p.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble() ?? 0;
                var length = p.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0;
                var size = p.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";

                var slopeInchPerFt = slope * 12;
                var status = slopeInchPerFt < minSlope && slopeInchPerFt > 0 ? "LOW_SLOPE"
                    : slopeInchPerFt == 0 ? "NO_SLOPE"
                    : "OK";

                return new
                {
                    id = p.Id.Value,
                    systemName = p.MEPSystem?.Name ?? "Unassigned",
                    size,
                    diameterMm = Math.Round(diameter * 304.8, 1),
                    slopeInchPerFt = Math.Round(slopeInchPerFt, 4),
                    slopePercent = Math.Round(slope * 100, 2),
                    lengthFt = Math.Round(length, 2),
                    status
                };
            }).ToList();

            var slopeIssues = analysis.Where(a => a.status != "OK").ToList();

            return new
            {
                totalDrainPipes = analysis.Count,
                slopeIssueCount = slopeIssues.Count,
                lowSlopeCount = slopeIssues.Count(i => i.status == "LOW_SLOPE"),
                noSlopeCount = slopeIssues.Count(i => i.status == "NO_SLOPE"),
                minSlopeRequirement = $"{minSlope} in/ft",
                issues = slopeIssues.Take(20).ToList(),
                sizeDistribution = analysis
                    .GroupBy(a => a.size)
                    .Select(g => new { size = g.Key, count = g.Count() })
                    .OrderByDescending(g => g.count)
                    .ToList()
            };
        });

        return SkillResult.Ok("Drainage analysis completed.", result);
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
