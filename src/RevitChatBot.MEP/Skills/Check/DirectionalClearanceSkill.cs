using Autodesk.Revit.DB;
using Nice3point.Revit.Extensions;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("check_directional_clearance",
    "Advanced directional clearance check using ray casting (ReferenceIntersector). " +
    "Casts rays from MEP elements in specified directions (Top/Bottom/Left/Right/Front/Back) " +
    "toward reference categories (Wall/Floor/Ceiling/Column/Beam). Reports violations where " +
    "actual distance is less than threshold. Supports linked models. Requires a 3D view.")]
[SkillParameter("mep_category", "string",
    "MEP category to check (duct/pipe/cabletray/conduit/all). Default: all",
    isRequired: false, allowedValues: ["duct", "pipe", "cabletray", "conduit", "all"])]
[SkillParameter("ref_category", "string",
    "Reference category to measure against (wall/floor/ceiling/column/beam/all). Default: all",
    isRequired: false, allowedValues: ["wall", "floor", "ceiling", "column", "beam", "all"])]
[SkillParameter("direction", "string",
    "Check direction (top/bottom/left/right/front/back/all). Default: all",
    isRequired: false, allowedValues: ["top", "bottom", "left", "right", "front", "back", "all"])]
[SkillParameter("threshold_mm", "number",
    "Minimum clearance distance in mm (default: 300)", isRequired: false)]
[SkillParameter("check_links", "boolean",
    "Include linked models in reference search (default: true)", isRequired: false)]
[SkillParameter("level_name", "string",
    "Filter MEP elements by level (optional)", isRequired: false)]
public class DirectionalClearanceSkill : ISkill
{
    private static readonly Dictionary<string, XYZ> DirectionVectors = new()
    {
        ["top"] = XYZ.BasisZ,
        ["bottom"] = -XYZ.BasisZ,
        ["left"] = -XYZ.BasisX,
        ["right"] = XYZ.BasisX,
        ["front"] = XYZ.BasisY,
        ["back"] = -XYZ.BasisY
    };

    private static readonly Dictionary<string, BuiltInCategory> MepCategories = new()
    {
        ["duct"] = BuiltInCategory.OST_DuctCurves,
        ["pipe"] = BuiltInCategory.OST_PipeCurves,
        ["cabletray"] = BuiltInCategory.OST_CableTray,
        ["conduit"] = BuiltInCategory.OST_Conduit,
    };

    private static readonly Dictionary<string, BuiltInCategory> RefCategories = new()
    {
        ["wall"] = BuiltInCategory.OST_Walls,
        ["floor"] = BuiltInCategory.OST_Floors,
        ["ceiling"] = BuiltInCategory.OST_Ceilings,
        ["column"] = BuiltInCategory.OST_StructuralColumns,
        ["beam"] = BuiltInCategory.OST_StructuralFraming,
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        string mepCat = (parameters.GetValueOrDefault("mep_category") as string)?.ToLowerInvariant() ?? "all";
        string refCat = (parameters.GetValueOrDefault("ref_category") as string)?.ToLowerInvariant() ?? "all";
        string direction = (parameters.GetValueOrDefault("direction") as string)?.ToLowerInvariant() ?? "all";
        double thresholdMm = ParseDouble(parameters.GetValueOrDefault("threshold_mm"), 300);
        bool checkLinks = ParseBool(parameters.GetValueOrDefault("check_links"), true);
        string? levelName = parameters.GetValueOrDefault("level_name") as string;

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var view3d = document.ActiveView as View3D;
            if (view3d is null)
            {
                view3d = new FilteredElementCollector(document)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .FirstOrDefault(v => !v.IsTemplate);
            }
            if (view3d is null)
                return new { error = "No 3D view available. A 3D view is required for ray casting." };

            var mepElements = CollectMepElements(document, mepCat, levelName);
            if (mepElements.Count == 0)
                return new { error = $"No MEP elements found for category '{mepCat}'." };

            var refFilter = BuildReferenceFilter(refCat);
            var intersector = new ReferenceIntersector(
                refFilter, FindReferenceTarget.Element, view3d);
            intersector.FindReferencesInRevitLinks = checkLinks;

            var directions = direction == "all"
                ? DirectionVectors.ToList()
                : DirectionVectors.Where(kv => kv.Key == direction).ToList();

            double thresholdFt = thresholdMm / 304.8;
            var violations = new List<object>();
            int totalChecks = 0;

            foreach (var elem in mepElements)
            {
                var origin = GetElementReferencePoint(elem);
                if (origin is null) continue;

                string catName = elem.Category?.Name ?? "Unknown";
                string size = elem.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";
                string sysName = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";
                string famInfo = GetFamilyInfo(document, elem);

                foreach (var (dirName, dirVector) in directions)
                {
                    totalChecks++;
                    var originPt = GetDirectionalOrigin(elem, dirName);
                    if (originPt is null) originPt = origin;

                    try
                    {
                        var hit = intersector.FindNearest(originPt, dirVector);
                        if (hit is null) continue;

                        var refPt = hit.GetReference().GlobalPoint;
                        double distFt = originPt.DistanceTo(refPt);
                        double distMm = distFt * 304.8;

                        if (distFt < thresholdFt)
                        {
                            var hitElemId = hit.GetReference().ElementId;
                            var hitElem = hitElemId.ToElement(document);
                            string hitCat = hitElem?.Category?.Name ?? "Unknown";

                            violations.Add(new
                            {
                                elementId = elem.Id.Value,
                                category = catName,
                                familyType = famInfo,
                                size,
                                system = sysName,
                                direction = dirName,
                                thresholdMm,
                                actualDistanceMm = Math.Round(distMm, 1),
                                status = "VIOLATION",
                                hitCategory = hitCat,
                                hitElementId = hitElemId.Value
                            });
                        }
                    }
                    catch { /* ray cast can fail for some geometries */ }
                }
            }

            var grouped = violations
                .Cast<dynamic>()
                .GroupBy(v => (string)v.direction)
                .Select(g => new { direction = g.Key, count = g.Count() })
                .ToList();

            return new
            {
                totalMepElements = mepElements.Count,
                totalChecks,
                directionsChecked = directions.Select(d => d.Key).ToList(),
                thresholdMm,
                checkLinkedModels = checkLinks,
                violationCount = violations.Count,
                violationsByDirection = grouped,
                violations = violations.Take(100).ToList()
            };
        });

        if (result is IDictionary<string, object> dict && dict.ContainsKey("error"))
            return SkillResult.Fail(dict["error"]?.ToString() ?? "Unknown error");

        return SkillResult.Ok("Directional clearance check completed.", result);
    }

    private static List<Element> CollectMepElements(Document doc, string category, string? levelName)
    {
        var cats = category == "all"
            ? MepCategories.Values.ToList()
            : MepCategories.TryGetValue(category, out var bic) ? [bic] : [];

        var elements = new List<Element>();
        foreach (var cat in cats)
        {
            elements.AddRange(doc.GetInstances(cat).ToList());
        }

        if (!string.IsNullOrEmpty(levelName))
        {
            elements = elements.Where(e =>
            {
                var lvlParam = e.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                if (lvlParam?.AsElementId() is not { } id || id == ElementId.InvalidElementId)
                    return false;
                return id.ToElement(doc)?.Name?.Equals(levelName, StringComparison.OrdinalIgnoreCase) == true;
            }).ToList();
        }

        return elements;
    }

    private static ElementFilter BuildReferenceFilter(string refCategory)
    {
        var cats = refCategory == "all"
            ? RefCategories.Values.ToList()
            : RefCategories.TryGetValue(refCategory, out var bic) ? [bic] : [];

        if (cats.Count == 0) cats = RefCategories.Values.ToList();

        var filters = cats.Select(c => new ElementCategoryFilter(c) as ElementFilter).ToList();
        return filters.Count == 1 ? filters[0] : new LogicalOrFilter(filters);
    }

    private static XYZ? GetElementReferencePoint(Element elem)
    {
        if (elem.Location is LocationCurve lc)
        {
            var curve = lc.Curve;
            return curve.Evaluate(0.5, true);
        }
        if (elem.Location is LocationPoint lp)
            return lp.Point;

        var bb = elem.get_BoundingBox(null);
        if (bb is not null)
            return (bb.Min + bb.Max) / 2;

        return null;
    }

    private static XYZ? GetDirectionalOrigin(Element elem, string direction)
    {
        if (elem.Location is not LocationCurve lc) return null;
        var p0 = lc.Curve.GetEndPoint(0);
        var p1 = lc.Curve.GetEndPoint(1);

        return direction switch
        {
            "top" => p0.Z >= p1.Z ? p0 : p1,
            "bottom" => p0.Z <= p1.Z ? p0 : p1,
            "left" => p0.X <= p1.X ? p0 : p1,
            "right" => p0.X >= p1.X ? p0 : p1,
            "front" => p0.Y >= p1.Y ? p0 : p1,
            "back" => p0.Y <= p1.Y ? p0 : p1,
            _ => null
        };
    }

    private static string GetFamilyInfo(Document doc, Element elem)
    {
        if (elem.GetTypeId().ToElement(doc) is ElementType et)
        {
            if (et is FamilySymbol fs) return $"{fs.FamilyName}: {fs.Name}";
            return et.Name;
        }
        return elem.Name ?? "Unknown";
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is long l) return l;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }

    private static bool ParseBool(object? value, bool fallback)
    {
        if (value is bool b) return b;
        if (value is string s)
            return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
        return fallback;
    }
}
