using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("check_pipe_slope",
    "Check pipe slope violations. Collects all pipes, reads RBS_PIPE_SLOPE (ratio), and finds " +
    "pipes with slope below the minimum. Returns violations with details.")]
[SkillParameter("minSlope", "number",
    "Minimum required slope in % (default: 0.5)", isRequired: false)]
public class CheckSlopeSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var minSlopePercent = ParseDouble(parameters.GetValueOrDefault("minSlope"), 0.5);
        var minSlopeRatio = minSlopePercent / 100.0;

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var pipes = new FilteredElementCollector(document)
                .OfClass(typeof(Pipe))
                .WhereElementIsNotElementType()
                .Cast<Pipe>()
                .ToList();

            var violations = new List<object>();
            foreach (var p in pipes)
            {
                var slopeParam = p.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE);
                var slopeRatio = slopeParam?.AsDouble() ?? 0;
                var slopePercent = slopeRatio * 100;

                if (slopeRatio >= minSlopeRatio) continue;

                var size = p.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";
                var systemName = p.MEPSystem?.Name ?? "Unassigned";
                var levelId = p.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId();
                var levelName = levelId is not null && levelId != ElementId.InvalidElementId
                    ? document.GetElement(levelId)?.Name ?? "N/A"
                    : "N/A";

                violations.Add(new
                {
                    elementId = p.Id.Value,
                    size,
                    actualSlopePercent = Math.Round(slopePercent, 2),
                    systemName,
                    level = levelName
                });
            }

            return new
            {
                totalPipes = pipes.Count,
                violationCount = violations.Count,
                minSlopePercent,
                violations
            };
        });

        return SkillResult.Ok("Pipe slope check completed.", result);
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
