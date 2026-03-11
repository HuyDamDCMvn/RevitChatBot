using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Check;

/// <summary>
/// Validates hanger/support spacing on ducts and pipes.
/// Checks that the distance between supports does not exceed the
/// maximum allowed spacing per engineering standards.
/// </summary>
[Skill("check_hanger_spacing",
    "Check hanger/support spacing on ducts and pipes. Finds segments that exceed " +
    "the maximum allowed distance between supports. Flags violations by system and level.")]
[SkillParameter("category", "string",
    "'duct', 'pipe', or 'all'. Default: 'all'.",
    isRequired: false, allowedValues: new[] { "duct", "pipe", "all" })]
[SkillParameter("max_spacing_mm", "number",
    "Maximum allowed spacing between supports in mm. Default: 3000 for ducts, 2000 for pipes.",
    isRequired: false)]
[SkillParameter("level", "string",
    "Filter by level name (optional).",
    isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' to check only elements visible in the current view, " +
    "'entire_model' to check all (default: entire_model)",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class CheckHangerSpacingSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var category = parameters.GetValueOrDefault("category")?.ToString() ?? "all";
        var maxSpacingMm = ParseDouble(parameters.GetValueOrDefault("max_spacing_mm"), 0);
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var violations = new List<object>();
            int totalChecked = 0;

            bool checkDucts = category is "duct" or "all";
            bool checkPipes = category is "pipe" or "all";

            if (checkDucts)
            {
                var ductMaxMm = maxSpacingMm > 0 ? maxSpacingMm : 3000;
                totalChecked += CheckSegments(document, typeof(Duct),
                    ductMaxMm, levelFilter, "Duct", violations);
            }

            if (checkPipes)
            {
                var pipeMaxMm = maxSpacingMm > 0 ? maxSpacingMm : 2000;
                totalChecked += CheckSegments(document, typeof(Pipe),
                    pipeMaxMm, levelFilter, "Pipe", violations);
            }

            return new
            {
                totalSegmentsChecked = totalChecked,
                violationCount = violations.Count,
                violations = violations.Take(100).ToList()
            };
        });

        return SkillResult.Ok("Hanger spacing check completed.", result);
    }

    private static int CheckSegments(
        Document doc, Type elementClass, double maxSpacingMm,
        string? levelFilter, string categoryLabel, List<object> violations)
    {
        var collector = new FilteredElementCollector(doc)
            .OfClass(elementClass)
            .WhereElementIsNotElementType();

        var elements = collector.ToList();

        var hangers = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_FabricationHangers)
            .WhereElementIsNotElementType()
            .ToList();

        var hangerLocations = hangers
            .Select(h => (h.Location as LocationPoint)?.Point)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        int count = 0;
        foreach (var elem in elements)
        {
            if (elem.Location is not LocationCurve locCurve) continue;

            var levelParam = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
            if (!string.IsNullOrWhiteSpace(levelFilter))
            {
                var lvlId = levelParam?.AsElementId();
                var lvlName = lvlId is not null && lvlId != ElementId.InvalidElementId
                    ? doc.GetElement(lvlId)?.Name ?? ""
                    : "";
                if (!lvlName.Contains(levelFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            count++;
            var curve = locCurve.Curve;
            var lengthFt = curve.Length;
            var lengthMm = lengthFt * 304.8;

            if (lengthMm <= maxSpacingMm) continue;

            var supportCount = CountSupportsOnSegment(curve, hangerLocations, 1.0);
            var requiredSupports = (int)Math.Ceiling(lengthMm / maxSpacingMm) - 1;

            if (supportCount < requiredSupports)
            {
                var systemName = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "N/A";
                var size = elem.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";

                var lvlId = levelParam?.AsElementId();
                var lvlName = lvlId is not null && lvlId != ElementId.InvalidElementId
                    ? doc.GetElement(lvlId)?.Name ?? "N/A" : "N/A";

                violations.Add(new
                {
                    elementId = elem.Id.Value,
                    type = categoryLabel,
                    size,
                    lengthMm = Math.Round(lengthMm, 0),
                    maxSpacingMm,
                    supportsFound = supportCount,
                    supportsRequired = requiredSupports,
                    system = systemName,
                    level = lvlName
                });
            }
        }

        return count;
    }

    private static int CountSupportsOnSegment(Curve curve, List<XYZ> hangerLocations, double toleranceFt)
    {
        int count = 0;
        foreach (var loc in hangerLocations)
        {
            var result = curve.Project(loc);
            if (result is not null && result.Distance <= toleranceFt)
                count++;
        }
        return count;
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
