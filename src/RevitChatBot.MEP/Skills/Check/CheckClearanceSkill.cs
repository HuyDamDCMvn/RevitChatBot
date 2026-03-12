using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Nice3point.Revit.Extensions;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("check_clearance",
    "Check elevation/clearance for ducts and pipes. Supports three modes: " +
    "'level' (height above reference level), 'ceiling' (headroom from ceiling down to MEP element), " +
    "'floor_above' (distance from floor above down to MEP element). " +
    "Returns violations where clearance is below the minimum threshold.")]
[SkillParameter("minHeight", "number",
    "Minimum clearance in meters. Default: 2.4 for 'level' mode, 0.3 for 'ceiling'/'floor_above'.",
    isRequired: false)]
[SkillParameter("category", "string",
    "Filter: 'all', 'ducts', or 'pipes' (default: all).", isRequired: false,
    allowedValues: new[] { "all", "ducts", "pipes" })]
[SkillParameter("reference", "string",
    "Reference surface: 'level' (height above level), 'ceiling' (distance from ceiling), " +
    "'floor_above' (distance from floor of level above). Default: 'level'.",
    isRequired: false, allowedValues: new[] { "level", "ceiling", "floor_above" })]
[SkillParameter("scope", "string",
    "Scope: 'active_view' or 'entire_model' (default: entire_model).",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class CheckClearanceSkill : ISkill
{
    private const double DefaultLevelMinHeight = 2.4;
    private const double DefaultHeadroomMin = 0.3;

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var reference = parameters.GetValueOrDefault("reference")?.ToString()?.ToLowerInvariant() ?? "level";
        var defaultMin = reference == "level" ? DefaultLevelMinHeight : DefaultHeadroomMin;
        var minHeightM = ParseDouble(parameters.GetValueOrDefault("minHeight"), defaultMin);
        var minHeightFt = minHeightM / 0.3048;
        var categoryFilter = parameters.GetValueOrDefault("category")?.ToString() ?? "all";
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var violations = new List<object>();

            List<BoundingBoxXYZ>? ceilingBoxes = null;
            Dictionary<long, double>? levelAboveElevations = null;

            if (reference == "ceiling")
                ceilingBoxes = CollectCeilingBoundingBoxes(document);
            else if (reference == "floor_above")
                levelAboveElevations = BuildLevelAboveElevationMap(document);

            var mepCurves = CollectMepCurves(document, scope, categoryFilter);

            foreach (var (curve, catName) in mepCurves)
            {
                var loc = curve.Location as LocationCurve;
                if (loc?.Curve is not { } line) continue;

                var midpoint = line.Evaluate(0.5, true);
                var topZ = curve.get_BoundingBox(null)?.Max.Z ?? midpoint.Z;

                double clearanceFt;
                string refName;

                switch (reference)
                {
                    case "ceiling":
                        var nearestCeilingZ = FindNearestCeilingAbove(midpoint, topZ, ceilingBoxes!);
                        if (nearestCeilingZ < 0) continue;
                        clearanceFt = nearestCeilingZ - topZ;
                        refName = "ceiling";
                        break;

                    case "floor_above":
                        var levelId = curve.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId();
                        if (levelId is null || levelId == ElementId.InvalidElementId) continue;
                        if (!levelAboveElevations!.TryGetValue(levelId.Value, out var floorAboveElev)) continue;
                        clearanceFt = floorAboveElev - topZ;
                        refName = "floor above";
                        break;

                    default:
                        var result = GetHeightAboveLevel(document, curve);
                        if (result.heightFt < 0) continue;
                        clearanceFt = result.heightFt;
                        refName = $"level ({result.levelName})";
                        break;
                }

                if (clearanceFt >= minHeightFt) continue;

                var clearanceM = clearanceFt * 0.3048;
                violations.Add(new
                {
                    elementId = curve.Id.Value,
                    category = catName,
                    size = curve.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A",
                    level = GetLevelName(document, curve),
                    reference = refName,
                    actualClearanceM = Math.Round(clearanceM, 2),
                    minRequiredM = minHeightM
                });
            }

            return new
            {
                referenceMode = reference,
                violationCount = violations.Count,
                minHeightM,
                categoryFilter,
                violations = violations.Take(100).ToList()
            };
        });

        return SkillResult.Ok("Clearance check completed.", result);
    }

    private static List<(MEPCurve curve, string category)> CollectMepCurves(
        Document doc, string scope, string categoryFilter)
    {
        var result = new List<(MEPCurve, string)>();

        if (categoryFilter is "all" or "ducts")
        {
            var ducts = ViewScopeHelper.CreateCollector(doc, scope)
                .OfClass(typeof(Duct))
                .WhereElementIsNotElementType()
                .Cast<Duct>()
                .Where(d => d.Location is LocationCurve);
            result.AddRange(ducts.Select(d => ((MEPCurve)d, "Duct")));
        }

        if (categoryFilter is "all" or "pipes")
        {
            var pipes = ViewScopeHelper.CreateCollector(doc, scope)
                .OfClass(typeof(Pipe))
                .WhereElementIsNotElementType()
                .Cast<Pipe>()
                .Where(p => p.Location is LocationCurve);
            result.AddRange(pipes.Select(p => ((MEPCurve)p, "Pipe")));
        }

        return result;
    }

    private static List<BoundingBoxXYZ> CollectCeilingBoundingBoxes(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Ceilings)
            .WhereElementIsNotElementType()
            .Select(c => c.get_BoundingBox(null))
            .Where(bb => bb is not null)
            .ToList()!;
    }

    private static Dictionary<long, double> BuildLevelAboveElevationMap(Document doc)
    {
        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .ToList();

        var map = new Dictionary<long, double>();
        for (int i = 0; i < levels.Count - 1; i++)
            map[levels[i].Id.Value] = levels[i + 1].Elevation;

        return map;
    }

    private static double FindNearestCeilingAbove(XYZ point, double elementTopZ, List<BoundingBoxXYZ> ceilingBoxes)
    {
        double nearest = -1;
        foreach (var bb in ceilingBoxes)
        {
            if (point.X < bb.Min.X || point.X > bb.Max.X) continue;
            if (point.Y < bb.Min.Y || point.Y > bb.Max.Y) continue;
            var ceilingBottomZ = bb.Min.Z;
            if (ceilingBottomZ <= elementTopZ) continue;

            if (nearest < 0 || ceilingBottomZ < nearest)
                nearest = ceilingBottomZ;
        }
        return nearest;
    }

    private static (double heightFt, string levelName) GetHeightAboveLevel(Document doc, MEPCurve curve)
    {
        var levelParam = curve.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
        var levelId = levelParam?.AsElementId();
        if (levelId is null || levelId == ElementId.InvalidElementId)
            return (-1, "N/A");

        var level = (levelId ?? ElementId.InvalidElementId).ToElement(doc) as Level;
        if (level is null) return (-1, "N/A");

        var loc = curve.Location as LocationCurve;
        if (loc?.Curve is not { } line)
            return (-1, level.Name);

        var midpoint = line.Evaluate(0.5, true);
        var heightFt = midpoint.Z - level.Elevation;
        return (heightFt, level.Name);
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var lvlId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId();
        if (lvlId is null || lvlId == ElementId.InvalidElementId) return "N/A";
        return doc.GetElement(lvlId)?.Name ?? "N/A";
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
