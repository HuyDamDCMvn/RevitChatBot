using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("check_clearance",
    "Check elevation/clearance conflicts. For each duct/pipe with LocationCurve, calculates midpoint Z " +
    "relative to reference level. Elements with height below minimum are violations.")]
[SkillParameter("minHeight", "number",
    "Minimum height above reference level in meters (default: 2.4)", isRequired: false)]
[SkillParameter("category", "string",
    "Filter: all, ducts, or pipes (default: all)", isRequired: false,
    allowedValues: new[] { "all", "ducts", "pipes" })]
public class CheckClearanceSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var minHeightM = ParseDouble(parameters.GetValueOrDefault("minHeight"), 2.4);
        var minHeightFt = minHeightM / 0.3048;
        var categoryFilter = parameters.GetValueOrDefault("category")?.ToString() ?? "all";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var violations = new List<object>();

            if (categoryFilter is "all" or "ducts")
            {
                var ducts = new FilteredElementCollector(document)
                    .OfClass(typeof(Duct))
                    .WhereElementIsNotElementType()
                    .Cast<Duct>()
                    .Where(d => d.Location is LocationCurve)
                    .ToList();

                foreach (var d in ducts)
                {
                    var (heightFt, levelName) = GetHeightAboveLevel(document, d);
                    if (heightFt < 0) continue;
                    if (heightFt >= minHeightFt) continue;

                    var heightM = heightFt * 0.3048;
                    var size = d.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";
                    violations.Add(new
                    {
                        elementId = d.Id.Value,
                        category = "Duct",
                        size,
                        level = levelName,
                        actualHeightM = Math.Round(heightM, 2)
                    });
                }
            }

            if (categoryFilter is "all" or "pipes")
            {
                var pipes = new FilteredElementCollector(document)
                    .OfClass(typeof(Pipe))
                    .WhereElementIsNotElementType()
                    .Cast<Pipe>()
                    .Where(p => p.Location is LocationCurve)
                    .ToList();

                foreach (var p in pipes)
                {
                    var (heightFt, levelName) = GetHeightAboveLevel(document, p);
                    if (heightFt < 0) continue;
                    if (heightFt >= minHeightFt) continue;

                    var heightM = heightFt * 0.3048;
                    var size = p.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";
                    violations.Add(new
                    {
                        elementId = p.Id.Value,
                        category = "Pipe",
                        size,
                        level = levelName,
                        actualHeightM = Math.Round(heightM, 2)
                    });
                }
            }

            return new
            {
                violationCount = violations.Count,
                minHeightM,
                categoryFilter,
                violations
            };
        });

        return SkillResult.Ok("Clearance check completed.", result);
    }

    private static (double heightFt, string levelName) GetHeightAboveLevel(Document doc, MEPCurve curve)
    {
        var levelParam = curve.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
        var levelId = levelParam?.AsElementId();
        if (levelId is null || levelId == ElementId.InvalidElementId)
            return (-1, "N/A");

        var level = doc.GetElement(levelId) as Level;
        if (level is null) return (-1, "N/A");

        var loc = curve.Location as LocationCurve;
        if (loc?.Curve is not { } line)
            return (-1, level.Name);

        var midpoint = line.Evaluate(0.5, true);
        var levelElev = level.Elevation;
        var heightFt = midpoint.Z - levelElev;
        return (heightFt, level.Name);
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
