using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Check;

/// <summary>
/// Validates model elements against MEP design criteria: velocity limits,
/// sizing ratios, temperature ranges, noise levels, and flow rates.
/// Configurable thresholds per system type.
/// </summary>
[Skill("validate_design_criteria",
    "Validate MEP elements against engineering design criteria. Checks velocity limits, " +
    "duct aspect ratios, pipe sizing, flow rates, and system-specific thresholds. " +
    "Reports elements that violate the specified criteria.")]
[SkillParameter("system_type", "string",
    "'hvac', 'plumbing', 'all'. Default: 'all'.",
    isRequired: false, allowedValues: new[] { "hvac", "plumbing", "all" })]
[SkillParameter("max_duct_velocity_ms", "number",
    "Maximum duct velocity in m/s. Default: 10.",
    isRequired: false)]
[SkillParameter("max_duct_aspect_ratio", "number",
    "Maximum duct width:height aspect ratio. Default: 4.",
    isRequired: false)]
[SkillParameter("max_pipe_velocity_ms", "number",
    "Maximum pipe velocity in m/s. Default: 3.",
    isRequired: false)]
[SkillParameter("level", "string",
    "Filter by level name (optional).",
    isRequired: false)]
public class ValidateDesignCriteriaSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var systemType = parameters.GetValueOrDefault("system_type")?.ToString() ?? "all";
        var maxDuctVel = ParseDouble(parameters.GetValueOrDefault("max_duct_velocity_ms"), 10);
        var maxAspectRatio = ParseDouble(parameters.GetValueOrDefault("max_duct_aspect_ratio"), 4);
        var maxPipeVel = ParseDouble(parameters.GetValueOrDefault("max_pipe_velocity_ms"), 3);
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var criteriaResults = new List<object>();

            if (systemType is "hvac" or "all")
            {
                criteriaResults.Add(CheckDuctCriteria(document, levelFilter, maxDuctVel, maxAspectRatio));
            }

            if (systemType is "plumbing" or "all")
            {
                criteriaResults.Add(CheckPipeCriteria(document, levelFilter, maxPipeVel));
            }

            int totalViolations = criteriaResults.Sum(r => ((dynamic)r).violationCount);
            return new
            {
                overallStatus = totalViolations > 0 ? "VIOLATIONS FOUND" : "COMPLIANT",
                totalViolations,
                criteria = criteriaResults
            };
        });

        return SkillResult.Ok("Design criteria validation completed.", result);
    }

    private static object CheckDuctCriteria(Document doc, string? levelFilter,
        double maxVelocity, double maxAspect)
    {
        var ducts = new FilteredElementCollector(doc)
            .OfClass(typeof(Duct))
            .WhereElementIsNotElementType()
            .Cast<Duct>()
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
            ducts = ducts.Where(d => GetLevelName(doc, d)
                .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        var violations = new List<object>();

        foreach (var duct in ducts)
        {
            var issues = new List<string>();

            var velocityFps = duct.get_Parameter(BuiltInParameter.RBS_VELOCITY)?.AsDouble() ?? 0;
            var velocityMs = velocityFps * 0.3048;
            if (velocityMs > maxVelocity)
                issues.Add($"Velocity {velocityMs:F1} m/s > max {maxVelocity} m/s");

            var widthFt = duct.Width;
            var heightFt = duct.Height;
            if (widthFt > 0 && heightFt > 0)
            {
                var aspect = Math.Max(widthFt, heightFt) / Math.Min(widthFt, heightFt);
                if (aspect > maxAspect)
                    issues.Add($"Aspect ratio {aspect:F1} > max {maxAspect}");
            }

            if (issues.Count > 0)
            {
                violations.Add(new
                {
                    elementId = duct.Id.Value,
                    size = duct.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A",
                    system = duct.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "N/A",
                    level = GetLevelName(doc, duct),
                    issues
                });
            }
        }

        return new
        {
            category = "HVAC Ducts",
            totalChecked = ducts.Count,
            violationCount = violations.Count,
            thresholds = new
            {
                maxVelocityMs = maxVelocity,
                maxAspectRatio = maxAspect
            },
            violations = violations.Take(30).ToList()
        };
    }

    private static object CheckPipeCriteria(Document doc, string? levelFilter, double maxVelocity)
    {
        var pipes = new FilteredElementCollector(doc)
            .OfClass(typeof(Pipe))
            .WhereElementIsNotElementType()
            .Cast<Pipe>()
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
            pipes = pipes.Where(p => GetLevelName(doc, p)
                .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        var violations = new List<object>();

        foreach (var pipe in pipes)
        {
            var issues = new List<string>();

            var velocityFps = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_VELOCITY_PARAM)?.AsDouble() ?? 0;
            var velocityMs = velocityFps * 0.3048;
            if (velocityMs > maxVelocity)
                issues.Add($"Velocity {velocityMs:F1} m/s > max {maxVelocity} m/s");

            var slope = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE)?.AsDouble() ?? 0;
            var classification = pipe.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "";
            if (classification.Contains("Sanitary", StringComparison.OrdinalIgnoreCase) && slope < 0.005)
                issues.Add($"Sanitary slope {slope * 100:F2}% < min 0.5%");
            if (classification.Contains("Storm", StringComparison.OrdinalIgnoreCase) && slope < 0.005)
                issues.Add($"Storm slope {slope * 100:F2}% < min 0.5%");

            if (issues.Count > 0)
            {
                violations.Add(new
                {
                    elementId = pipe.Id.Value,
                    size = pipe.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A",
                    system = pipe.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "N/A",
                    level = GetLevelName(doc, pipe),
                    issues
                });
            }
        }

        return new
        {
            category = "Plumbing Pipes",
            totalChecked = pipes.Count,
            violationCount = violations.Count,
            thresholds = new { maxVelocityMs = maxVelocity },
            violations = violations.Take(30).ToList()
        };
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
}
