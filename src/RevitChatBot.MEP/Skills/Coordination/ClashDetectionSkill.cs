using Autodesk.Revit.DB;
using Nice3point.Revit.Extensions;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Coordination;

[Skill("clash_detection",
    "Detect geometric clashes/intersections between elements in the model. " +
    "Checks bounding box overlaps between two categories. Supports MEP-to-MEP, " +
    "MEP-to-structural (beams, columns), MEP-to-architectural (walls, floors).")]
[SkillParameter("category_a", "string",
    "First category: duct, pipe, equipment, cable_tray, conduit, fitting",
    isRequired: true,
    allowedValues: new[] { "duct", "pipe", "equipment", "cable_tray", "conduit", "fitting" })]
[SkillParameter("category_b", "string",
    "Second category: duct, pipe, equipment, cable_tray, conduit, fitting, " +
    "beam, column, wall, floor",
    isRequired: true,
    allowedValues: new[] { "duct", "pipe", "equipment", "cable_tray", "conduit", "fitting",
        "beam", "column", "wall", "floor" })]
[SkillParameter("tolerance_feet", "number",
    "Clash tolerance in feet (default 0.01)",
    isRequired: false)]
[SkillParameter("level", "string",
    "Filter both sets by level name (optional).",
    isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' or 'entire_model' (default: entire_model).",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class ClashDetectionSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory> CategoryMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["duct"] = BuiltInCategory.OST_DuctCurves,
        ["pipe"] = BuiltInCategory.OST_PipeCurves,
        ["equipment"] = BuiltInCategory.OST_MechanicalEquipment,
        ["cable_tray"] = BuiltInCategory.OST_CableTray,
        ["conduit"] = BuiltInCategory.OST_Conduit,
        ["fitting"] = BuiltInCategory.OST_DuctFitting,
        ["beam"] = BuiltInCategory.OST_StructuralFraming,
        ["column"] = BuiltInCategory.OST_StructuralColumns,
        ["wall"] = BuiltInCategory.OST_Walls,
        ["floor"] = BuiltInCategory.OST_Floors,
        ["structural"] = BuiltInCategory.OST_StructuralColumns,
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var catA = parameters.GetValueOrDefault("category_a")?.ToString() ?? "duct";
        var catB = parameters.GetValueOrDefault("category_b")?.ToString() ?? "pipe";
        var tolerance = 0.01;
        if (parameters.TryGetValue("tolerance_feet", out var tol) && tol is not null)
            double.TryParse(tol.ToString(), out tolerance);
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        if (!CategoryMapping.ContainsKey(catA))
            return SkillResult.Fail($"Unknown category_a '{catA}'. Supported: {string.Join(", ", CategoryMapping.Keys)}");
        if (!CategoryMapping.ContainsKey(catB))
            return SkillResult.Fail($"Unknown category_b '{catB}'. Supported: {string.Join(", ", CategoryMapping.Keys)}");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elementsA = GetElements(document, catA, scope, levelFilter);
            var elementsB = GetElements(document, catB, scope, levelFilter);

            var clashes = new List<object>();

            foreach (var a in elementsA)
            {
                var bbA = a.get_BoundingBox(null);
                if (bbA is null) continue;

                var expandedMin = new XYZ(bbA.Min.X - tolerance, bbA.Min.Y - tolerance, bbA.Min.Z - tolerance);
                var expandedMax = new XYZ(bbA.Max.X + tolerance, bbA.Max.Y + tolerance, bbA.Max.Z + tolerance);

                foreach (var b in elementsB)
                {
                    if (a.Id == b.Id) continue;
                    var bbB = b.get_BoundingBox(null);
                    if (bbB is null) continue;

                    if (BoundingBoxesOverlap(expandedMin, expandedMax, bbB.Min, bbB.Max))
                    {
                        clashes.Add(new
                        {
                            elementA = new { Id = a.Id.Value, Name = a.Name, Category = a.Category?.Name },
                            elementB = new { Id = b.Id.Value, Name = b.Name, Category = b.Category?.Name }
                        });
                    }
                }
            }

            return new
            {
                categoryA = catA,
                categoryB = catB,
                elementsInA = elementsA.Count,
                elementsInB = elementsB.Count,
                totalClashes = clashes.Count,
                clashes = clashes.Take(100).ToList()
            };
        });

        return SkillResult.Ok("Clash detection completed.", result);
    }

    private static List<Element> GetElements(Document doc, string category, string scope, string? levelFilter)
    {
        if (!CategoryMapping.TryGetValue(category, out var bic))
            bic = BuiltInCategory.OST_GenericModel;

        var elements = ViewScopeHelper.CreateCollector(doc, scope)
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
        {
            elements = elements.Where(e =>
            {
                var lvlName = GetLevelName(doc, e);
                return lvlName.Contains(levelFilter, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        }

        return elements;
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var lvlId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId()
                    ?? elem.LevelId;
        if (lvlId is null || lvlId == ElementId.InvalidElementId) return "";
        return doc.GetElement(lvlId)?.Name ?? "";
    }

    private static bool BoundingBoxesOverlap(XYZ minA, XYZ maxA, XYZ minB, XYZ maxB) =>
        minA.X <= maxB.X && maxA.X >= minB.X &&
        minA.Y <= maxB.Y && maxA.Y >= minB.Y &&
        minA.Z <= maxB.Z && maxA.Z >= minB.Z;
}
