using Autodesk.Revit.DB;
using Nice3point.Revit.Extensions;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Coordination;

[Skill("clash_detection",
    "Detect geometric clashes/intersections between MEP elements in the model. " +
    "Checks bounding box overlaps between two categories of elements.")]
[SkillParameter("category_a", "string",
    "First category: duct, pipe, equipment",
    isRequired: true,
    allowedValues: new[] { "duct", "pipe", "equipment" })]
[SkillParameter("category_b", "string",
    "Second category: duct, pipe, equipment, structural",
    isRequired: true,
    allowedValues: new[] { "duct", "pipe", "equipment", "structural" })]
[SkillParameter("tolerance_feet", "number",
    "Clash tolerance in feet (default 0.01)",
    isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' to limit to elements visible in the current view, " +
    "'entire_model' to include all (default: entire_model)",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class ClashDetectionSkill : ISkill
{
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
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elementsA = GetElements(document, catA, scope);
            var elementsB = GetElements(document, catB, scope);

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
                totalClashes = clashes.Count,
                clashes = clashes.Take(50).ToList()
            };
        });

        return SkillResult.Ok("Clash detection completed.", result);
    }

    private static List<Element> GetElements(Document doc, string category, string scope)
    {
        var bic = category switch
        {
            "duct" => BuiltInCategory.OST_DuctCurves,
            "pipe" => BuiltInCategory.OST_PipeCurves,
            "equipment" => BuiltInCategory.OST_MechanicalEquipment,
            "structural" => BuiltInCategory.OST_StructuralColumns,
            _ => BuiltInCategory.OST_GenericModel
        };

        return ViewScopeHelper.CreateCollector(doc, scope)
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .ToList();
    }

    private static bool BoundingBoxesOverlap(XYZ minA, XYZ maxA, XYZ minB, XYZ maxB) =>
        minA.X <= maxB.X && maxA.X >= minB.X &&
        minA.Y <= maxB.Y && maxA.Y >= minB.Y &&
        minA.Z <= maxB.Z && maxA.Z >= minB.Z;
}
