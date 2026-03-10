using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Plumbing;

/// <summary>
/// Analyzes pipe sizing: checks velocity, diameter distribution, and identifies sizing issues.
/// </summary>
[Skill("pipe_sizing_analysis",
    "Analyze pipe sizing in the model. Check for velocity issues and review diameter distribution. " +
    "Returns size, flow, velocity, and sizing assessment per pipe.")]
[SkillParameter("system_name", "string", "Filter by system name (optional)", isRequired: false)]
[SkillParameter("max_velocity_fps", "number",
    "Maximum allowed velocity in ft/s (default: 8 for supply, 4 for drain)", isRequired: false)]
[SkillParameter("pipe_type", "string",
    "Filter by pipe system type", isRequired: false,
    allowedValues: new[] { "supply", "return", "sanitary", "storm", "all" })]
public class PipeSizingSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var systemName = parameters.GetValueOrDefault("system_name")?.ToString();
        var maxVel = ParseDouble(parameters.GetValueOrDefault("max_velocity_fps"), 8);
        var pipeType = parameters.GetValueOrDefault("pipe_type")?.ToString() ?? "all";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var pipes = new FilteredElementCollector(document)
                .OfClass(typeof(Pipe))
                .WhereElementIsNotElementType()
                .Cast<Pipe>()
                .ToList();

            if (systemName is not null)
                pipes = pipes.Where(p =>
                    p.MEPSystem?.Name?.Contains(systemName, StringComparison.OrdinalIgnoreCase) == true).ToList();

            if (pipeType != "all")
            {
                pipes = pipes.Where(p =>
                {
                    var sysType = (p.MEPSystem as PipingSystem)?.SystemType;
                    return pipeType switch
                    {
                        "supply" => sysType == PipeSystemType.SupplyHydronic || sysType == PipeSystemType.DomesticColdWater || sysType == PipeSystemType.DomesticHotWater,
                        "return" => sysType == PipeSystemType.ReturnHydronic,
                        "sanitary" => sysType == PipeSystemType.Sanitary,
                        "storm" => sysType == PipeSystemType.OtherPipe,
                        _ => true
                    };
                }).ToList();
            }

            var analysis = pipes.Select(p =>
            {
                var size = p.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";
                var diameter = p.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble() ?? 0;
                var velocity = p.get_Parameter(BuiltInParameter.RBS_PIPE_VELOCITY_PARAM)?.AsDouble() ?? 0;
                var flow = p.get_Parameter(BuiltInParameter.RBS_PIPE_FLOW_PARAM)?.AsDouble() ?? 0;
                var length = p.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0;

                var status = velocity > maxVel ? "HIGH_VELOCITY" : "OK";

                return new
                {
                    id = p.Id.Value,
                    systemName = p.MEPSystem?.Name ?? "Unassigned",
                    size,
                    diameterInch = Math.Round(diameter * 12, 2),
                    diameterMm = Math.Round(diameter * 304.8, 1),
                    flowGPM = Math.Round(flow, 2),
                    flowLps = Math.Round(flow * 0.0631, 3),
                    velocityFps = Math.Round(velocity, 2),
                    velocityMps = Math.Round(velocity * 0.3048, 2),
                    lengthFt = Math.Round(length, 2),
                    status
                };
            }).ToList();

            var sizeDistribution = analysis
                .GroupBy(a => a.size)
                .Select(g => new { size = g.Key, count = g.Count(), totalLengthFt = Math.Round(g.Sum(p => p.lengthFt), 1) })
                .OrderByDescending(g => g.count)
                .ToList();

            var issues = analysis.Where(a => a.status != "OK").ToList();

            return new
            {
                totalPipes = analysis.Count,
                issueCount = issues.Count,
                highVelocityCount = issues.Count,
                maxVelocityLimit = maxVel,
                sizeDistribution,
                issues = issues.Take(20).ToList()
            };
        });

        return SkillResult.Ok("Pipe sizing analysis completed.", result);
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
