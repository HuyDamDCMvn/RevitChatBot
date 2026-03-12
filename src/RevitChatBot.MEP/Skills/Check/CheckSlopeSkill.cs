using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("check_pipe_slope",
    "Check slope violations for pipes and/or ducts. Reads RBS_PIPE_SLOPE parameter and " +
    "finds elements with slope below the minimum or with reversed (negative) slope. " +
    "Supports both pipe drainage slope and duct condensate slope checks.")]
[SkillParameter("minSlope", "number",
    "Minimum required slope in % (default: 0.5 for pipes, 0 for ducts).", isRequired: false)]
[SkillParameter("category", "string",
    "Which elements: 'pipe', 'duct', or 'all'. Default: 'pipe'.",
    isRequired: false, allowedValues: new[] { "pipe", "duct", "all" })]
[SkillParameter("check_reverse", "string",
    "Check for reversed (negative) slope: 'true' or 'false'. Default: 'true'.",
    isRequired: false)]
[SkillParameter("system_name", "string",
    "Filter by system name (optional).", isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' or 'entire_model' (default: entire_model).",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class CheckSlopeSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var category = parameters.GetValueOrDefault("category")?.ToString()?.ToLowerInvariant() ?? "pipe";
        var defaultMin = category == "duct" ? 0.0 : 0.5;
        var minSlopePercent = ParseDouble(parameters.GetValueOrDefault("minSlope"), defaultMin);
        var minSlopeRatio = minSlopePercent / 100.0;
        var checkReverse = parameters.GetValueOrDefault("check_reverse")?.ToString() != "false";
        var systemFilter = parameters.GetValueOrDefault("system_name")?.ToString();
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var violations = new List<object>();
            int totalPipes = 0, totalDucts = 0;

            if (category is "pipe" or "all")
            {
                var pipes = ViewScopeHelper.CreateCollector(document, scope)
                    .OfClass(typeof(Pipe))
                    .WhereElementIsNotElementType()
                    .Cast<Pipe>()
                    .ToList();

                if (!string.IsNullOrWhiteSpace(systemFilter))
                    pipes = pipes.Where(p => p.MEPSystem?.Name?.Contains(systemFilter, StringComparison.OrdinalIgnoreCase) == true).ToList();

                totalPipes = pipes.Count;
                foreach (var p in pipes)
                {
                    var slopeRatio = p.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE)?.AsDouble() ?? 0;
                    var slopePercent = slopeRatio * 100;

                    var isViolation = slopeRatio < minSlopeRatio;
                    var isReversed = checkReverse && slopeRatio < 0;

                    if (!isViolation && !isReversed) continue;

                    violations.Add(new
                    {
                        elementId = p.Id.Value,
                        elementCategory = "Pipe",
                        size = p.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A",
                        actualSlopePercent = Math.Round(slopePercent, 3),
                        isReversed,
                        systemName = p.MEPSystem?.Name ?? "Unassigned",
                        level = GetLevelName(document, p)
                    });
                }
            }

            if (category is "duct" or "all")
            {
                var ducts = ViewScopeHelper.CreateCollector(document, scope)
                    .OfClass(typeof(Duct))
                    .WhereElementIsNotElementType()
                    .Cast<Duct>()
                    .ToList();

                if (!string.IsNullOrWhiteSpace(systemFilter))
                    ducts = ducts.Where(d => d.MEPSystem?.Name?.Contains(systemFilter, StringComparison.OrdinalIgnoreCase) == true).ToList();

                totalDucts = ducts.Count;
                foreach (var d in ducts)
                {
                    if (d.Location is not LocationCurve lc) continue;
                    var curve = lc.Curve;
                    var startZ = curve.GetEndPoint(0).Z;
                    var endZ = curve.GetEndPoint(1).Z;
                    var length2d = Math.Sqrt(
                        Math.Pow(curve.GetEndPoint(1).X - curve.GetEndPoint(0).X, 2) +
                        Math.Pow(curve.GetEndPoint(1).Y - curve.GetEndPoint(0).Y, 2));

                    if (length2d < 0.01) continue;

                    var slopeRatio = (endZ - startZ) / length2d;
                    var slopePercent = slopeRatio * 100;
                    var absSlopeRatio = Math.Abs(slopeRatio);

                    var isViolation = minSlopeRatio > 0 && absSlopeRatio < minSlopeRatio;
                    var isReversed = checkReverse && slopeRatio < -0.001;

                    if (!isViolation && !isReversed) continue;

                    violations.Add(new
                    {
                        elementId = d.Id.Value,
                        elementCategory = "Duct",
                        size = d.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A",
                        actualSlopePercent = Math.Round(slopePercent, 3),
                        isReversed,
                        systemName = d.MEPSystem?.Name ?? "Unassigned",
                        level = GetLevelName(document, d)
                    });
                }
            }

            return new
            {
                totalPipes,
                totalDucts,
                totalChecked = totalPipes + totalDucts,
                violationCount = violations.Count,
                minSlopePercent,
                checkReverse,
                violations = violations.Take(100).ToList()
            };
        });

        return SkillResult.Ok("Slope check completed.", result);
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var levelId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId();
        if (levelId is null || levelId == ElementId.InvalidElementId) return "N/A";
        return doc.GetElement(levelId)?.Name ?? "N/A";
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
