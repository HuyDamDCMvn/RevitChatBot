using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Coordination;

/// <summary>
/// Coordinates MEP elements across linked Revit models.
/// Detects potential clashes between the host model and linked models,
/// analyzes spatial overlaps, and reports coordination issues.
/// </summary>
[Skill("multi_model_coordination",
    "Coordinate MEP elements across linked Revit models. Detects potential spatial " +
    "conflicts between the host model's MEP elements and linked models. " +
    "Reports clashes by category, severity, and location.")]
[SkillParameter("host_category", "string",
    "Host model category to check: 'duct', 'pipe', 'equipment', 'all'. Default: 'all'.",
    isRequired: false, allowedValues: new[] { "duct", "pipe", "equipment", "all" })]
[SkillParameter("tolerance_mm", "number",
    "Clash tolerance in mm. Elements closer than this are flagged. Default: 50.",
    isRequired: false)]
[SkillParameter("level", "string",
    "Filter by level name (optional).",
    isRequired: false)]
public class MultiModelCoordinationSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var hostCategory = parameters.GetValueOrDefault("host_category")?.ToString() ?? "all";
        var toleranceMm = ParseDouble(parameters.GetValueOrDefault("tolerance_mm"), 50);
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();

        var toleranceFt = toleranceMm / 304.8;

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var links = new FilteredElementCollector(document)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            if (links.Count == 0)
                return new
                {
                    message = "No linked models found in this document.",
                    linkedModels = 0,
                    clashes = new List<object>()
                };

            var hostElements = CollectHostElements(document, hostCategory, levelFilter);

            var hostBbs = hostElements
                .Select(e => new { Element = e, BB = e.get_BoundingBox(null) })
                .Where(x => x.BB is not null)
                .ToList();

            var clashes = new List<object>();
            var linkSummaries = new List<object>();

            foreach (var link in links)
            {
                var linkDoc = link.GetLinkDocument();
                if (linkDoc is null) continue;

                var transform = link.GetTotalTransform();
                int linkClashCount = 0;

                var linkStructural = new FilteredElementCollector(linkDoc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .ToList();

                var linkWalls = new FilteredElementCollector(linkDoc)
                    .OfClass(typeof(Wall))
                    .WhereElementIsNotElementType()
                    .ToList();

                var linkFloors = new FilteredElementCollector(linkDoc)
                    .OfClass(typeof(Floor))
                    .WhereElementIsNotElementType()
                    .ToList();

                var allLinkElements = linkStructural.Concat(linkWalls).Concat(linkFloors);

                var linkBbs = allLinkElements
                    .Select(le =>
                    {
                        var bb = le.get_BoundingBox(null);
                        if (bb is null) return null;
                        return new
                        {
                            Element = le,
                            BB = TransformBB(bb, transform)
                        };
                    })
                    .Where(x => x is not null)
                    .ToList();

                foreach (var host in hostBbs)
                {
                    var expanded = ExpandBB(host.BB!, toleranceFt);

                    foreach (var linked in linkBbs)
                    {
                        if (!BoundingBoxesOverlap(expanded, linked!.BB))
                            continue;

                        linkClashCount++;
                        if (clashes.Count < 100)
                        {
                            clashes.Add(new
                            {
                                hostElementId = host.Element.Id.Value,
                                hostCategory = host.Element.Category?.Name ?? "Unknown",
                                hostLevel = GetLevelName(document, host.Element),
                                linkedModel = linkDoc.Title,
                                linkedElementId = linked.Element.Id.Value,
                                linkedCategory = linked.Element.Category?.Name ?? "Unknown",
                                severity = "potential_clash"
                            });
                        }
                    }
                }

                linkSummaries.Add(new
                {
                    linkedModel = linkDoc.Title,
                    structuralElements = linkStructural.Count,
                    walls = linkWalls.Count,
                    floors = linkFloors.Count,
                    clashCount = linkClashCount
                });
            }

            return new
            {
                linkedModels = links.Count,
                hostElementsChecked = hostBbs.Count,
                totalClashes = clashes.Count,
                toleranceMm,
                linkSummaries,
                clashes
            };
        });

        return SkillResult.Ok("Multi-model coordination check completed.", result);
    }

    private static List<Element> CollectHostElements(Document doc, string category, string? levelFilter)
    {
        var elements = new List<Element>();

        if (category is "duct" or "all")
            elements.AddRange(new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_DuctCurves)
                .WhereElementIsNotElementType().ToList());
        if (category is "pipe" or "all")
            elements.AddRange(new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType().ToList());
        if (category is "equipment" or "all")
            elements.AddRange(new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                .WhereElementIsNotElementType().ToList());

        if (!string.IsNullOrWhiteSpace(levelFilter))
            elements = elements.Where(e => GetLevelName(doc, e)
                .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        return elements;
    }

    private static BoundingBoxXYZ TransformBB(BoundingBoxXYZ bb, Transform transform)
    {
        var corners = new[]
        {
            transform.OfPoint(new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z)),
            transform.OfPoint(new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z)),
            transform.OfPoint(new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z)),
            transform.OfPoint(new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z))
        };

        return new BoundingBoxXYZ
        {
            Min = new XYZ(corners.Min(c => c.X), corners.Min(c => c.Y), corners.Min(c => c.Z)),
            Max = new XYZ(corners.Max(c => c.X), corners.Max(c => c.Y), corners.Max(c => c.Z))
        };
    }

    private static BoundingBoxXYZ ExpandBB(BoundingBoxXYZ bb, double tolerance)
    {
        return new BoundingBoxXYZ
        {
            Min = new XYZ(bb.Min.X - tolerance, bb.Min.Y - tolerance, bb.Min.Z - tolerance),
            Max = new XYZ(bb.Max.X + tolerance, bb.Max.Y + tolerance, bb.Max.Z + tolerance)
        };
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

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
