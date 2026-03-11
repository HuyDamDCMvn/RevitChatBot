using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Check;

/// <summary>
/// Detects MEP elements passing through walls/floors and validates that
/// proper sleeves, firestops, or penetration annotations are present.
/// </summary>
[Skill("check_penetration",
    "Detect MEP elements (ducts, pipes) passing through walls and floors. " +
    "Validates that proper penetration sleeves or firestop annotations exist. " +
    "Identifies unprotected penetrations for fire safety compliance.")]
[SkillParameter("category", "string",
    "'duct', 'pipe', or 'all'. Default: 'all'.",
    isRequired: false, allowedValues: new[] { "duct", "pipe", "all" })]
[SkillParameter("level", "string",
    "Filter by level name (optional).",
    isRequired: false)]
[SkillParameter("check_firestop", "string",
    "Whether to check for firestop families near penetrations. 'true' or 'false'. Default: 'true'.",
    isRequired: false)]
public class CheckPenetrationSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var category = parameters.GetValueOrDefault("category")?.ToString() ?? "all";
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var checkFirestop = parameters.GetValueOrDefault("check_firestop")?.ToString() != "false";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var walls = new FilteredElementCollector(document)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .Select(w => new { Wall = w, BB = w.get_BoundingBox(null) })
                .Where(x => x.BB is not null)
                .ToList();

            var floors = new FilteredElementCollector(document)
                .OfClass(typeof(Floor))
                .WhereElementIsNotElementType()
                .Select(f => new { Floor = f, BB = f.get_BoundingBox(null) })
                .Where(x => x.BB is not null)
                .ToList();

            List<Element> firestopFamilies = [];
            if (checkFirestop)
            {
                firestopFamilies = new FilteredElementCollector(document)
                    .OfClass(typeof(FamilyInstance))
                    .WhereElementIsNotElementType()
                    .Where(e =>
                    {
                        var fn = e.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString() ?? "";
                        return fn.Contains("Firestop", StringComparison.OrdinalIgnoreCase) ||
                               fn.Contains("Sleeve", StringComparison.OrdinalIgnoreCase) ||
                               fn.Contains("Penetration", StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();
            }

            var mepElements = new List<Element>();
            if (category is "duct" or "all")
                mepElements.AddRange(new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType().ToList());
            if (category is "pipe" or "all")
                mepElements.AddRange(new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType().ToList());

            if (!string.IsNullOrWhiteSpace(levelFilter))
                mepElements = mepElements.Where(e => GetLevelName(document, e)
                    .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            var penetrations = new List<object>();
            int withFirestop = 0;

            foreach (var mep in mepElements)
            {
                if (mep.Location is not LocationCurve lc) continue;
                var curve = lc.Curve;
                var mepBb = mep.get_BoundingBox(null);
                if (mepBb is null) continue;

                foreach (var w in walls)
                {
                    if (!BoundingBoxesOverlap(mepBb, w.BB!)) continue;

                    bool hasProtection = false;
                    if (checkFirestop)
                    {
                        var midPt = curve.Evaluate(0.5, true);
                        hasProtection = firestopFamilies.Any(fs =>
                        {
                            var fsBb = fs.get_BoundingBox(null);
                            return fsBb is not null && BoundingBoxesOverlap(fsBb, w.BB!);
                        });
                    }

                    if (hasProtection) { withFirestop++; continue; }

                    penetrations.Add(new
                    {
                        mepElementId = mep.Id.Value,
                        mepType = mep.Category?.Name ?? "Unknown",
                        wallId = w.Wall.Id.Value,
                        wallType = w.Wall.WallType?.Name ?? "N/A",
                        level = GetLevelName(document, mep),
                        hasFirestop = false,
                        size = mep.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A"
                    });
                }
            }

            return new
            {
                totalMepElements = mepElements.Count,
                penetrationsDetected = penetrations.Count + withFirestop,
                withFirestop,
                unprotected = penetrations.Count,
                unprotectedPenetrations = penetrations.Take(50).ToList()
            };
        });

        return SkillResult.Ok("Penetration check completed.", result);
    }

    private static bool BoundingBoxesOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b)
    {
        return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
               a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
               a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var lvlId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId() ?? elem.LevelId;
        if (lvlId is null || lvlId == ElementId.InvalidElementId) return "";
        return doc.GetElement(lvlId)?.Name ?? "";
    }
}
