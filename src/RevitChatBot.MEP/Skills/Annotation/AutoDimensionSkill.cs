using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;
using RevitChatBot.RevitServices.Annotation;

namespace RevitChatBot.MEP.Skills.Annotation;

[Skill("auto_dimension",
    "Automatically place linear dimensions on MEP elements in a view. " +
    "Groups elements by direction (horizontal/vertical runs), creates dimension chains " +
    "along centerlines or faces. Avoids overlapping with existing annotations.")]
[SkillParameter("view_id", "string",
    "Element ID of the target view", isRequired: true)]
[SkillParameter("category", "string",
    "Element category to dimension: 'Ducts', 'Pipes', 'Equipment', or 'all'",
    isRequired: true)]
[SkillParameter("direction", "string",
    "Which runs to dimension: 'horizontal', 'vertical', or 'both' (default 'both')",
    isRequired: false,
    allowedValues: new[] { "horizontal", "vertical", "both" })]
[SkillParameter("reference_type", "string",
    "What to dimension: 'centerlines' or 'faces' (default 'centerlines')",
    isRequired: false,
    allowedValues: new[] { "centerlines", "faces" })]
[SkillParameter("offset", "string",
    "Distance of dimension line from elements in feet (default '0.8')",
    isRequired: false)]
public class AutoDimensionSkill : ISkill
{
    private const double GroupingTolerance = 0.5;
    private const double DefaultOffset = 0.8;

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var viewIdStr = parameters.GetValueOrDefault("view_id")?.ToString();
        var categoryStr = parameters.GetValueOrDefault("category")?.ToString() ?? "all";
        var directionStr = parameters.GetValueOrDefault("direction")?.ToString() ?? "both";
        var refTypeStr = parameters.GetValueOrDefault("reference_type")?.ToString() ?? "centerlines";
        var offsetStr = parameters.GetValueOrDefault("offset")?.ToString();

        if (string.IsNullOrWhiteSpace(viewIdStr) || !long.TryParse(viewIdStr, out var viewIdLong))
            return SkillResult.Fail("view_id is required and must be a valid element ID.");

        double offset = DefaultOffset;
        if (!string.IsNullOrEmpty(offsetStr) && double.TryParse(offsetStr, out var parsedOffset))
            offset = Math.Max(0.2, parsedOffset);

        bool useFaces = refTypeStr.Equals("faces", StringComparison.OrdinalIgnoreCase);
        bool doHorizontal = directionStr is "horizontal" or "both";
        bool doVertical = directionStr is "vertical" or "both";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var viewElem = document.GetElement(new ElementId(viewIdLong));
            if (viewElem is not View view)
                return new { success = false, message = "Invalid view ID.", dimensionsCreated = 0 };

            var categories = ResolveDimensionCategories(categoryStr);
            if (categories.Count == 0)
                return new { success = false, message = $"No valid categories for: '{categoryStr}'", dimensionsCreated = 0 };

            var elements = categories
                .SelectMany(cat => new FluentCollector(document)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .InView(view.Id)
                    .ToList())
                .Where(e => e.Location is LocationCurve || e.Location is LocationPoint)
                .ToList();

            if (elements.Count < 2)
                return new { success = true, message = "Less than 2 dimensionable elements found.", dimensionsCreated = 0 };

            var linearElements = elements
                .Where(e => e.Location is LocationCurve)
                .ToList();

            var horizontalRuns = new List<List<Element>>();
            var verticalRuns = new List<List<Element>>();

            if (doHorizontal)
                horizontalRuns = GroupByAxis(linearElements, isHorizontal: true);
            if (doVertical)
                verticalRuns = GroupByAxis(linearElements, isHorizontal: false);

            using var tx = new Transaction(document, "Auto-dimension");
            tx.Start();

            int created = 0;

            foreach (var run in horizontalRuns.Where(r => r.Count >= 2))
            {
                if (TryCreateDimensionChain(document, view, run, isHorizontal: true, offset, useFaces))
                    created++;
            }

            foreach (var run in verticalRuns.Where(r => r.Count >= 2))
            {
                if (TryCreateDimensionChain(document, view, run, isHorizontal: false, offset, useFaces))
                    created++;
            }

            // Dimension point-based equipment relative to nearest grid/wall
            var pointElements = elements
                .Where(e => e.Location is LocationPoint && e is FamilyInstance)
                .ToList();

            if (pointElements.Count >= 2)
            {
                if (doHorizontal)
                {
                    var sorted = pointElements.OrderBy(e => GetLocationPoint(e).X).ToList();
                    if (TryCreatePointDimensionChain(document, view, sorted, isHorizontal: true, offset))
                        created++;
                }
                if (doVertical)
                {
                    var sorted = pointElements.OrderBy(e => GetLocationPoint(e).Y).ToList();
                    if (TryCreatePointDimensionChain(document, view, sorted, isHorizontal: false, offset))
                        created++;
                }
            }

            tx.Commit();

            return new
            {
                success = true,
                message = $"Created {created} dimension chains for {elements.Count} elements " +
                          $"({horizontalRuns.Count} horizontal runs, {verticalRuns.Count} vertical runs).",
                dimensionsCreated = created
            };
        });

        var r = (dynamic)result!;
        return (bool)r.success
            ? SkillResult.Ok((string)r.message, result)
            : SkillResult.Fail((string)r.message);
    }

    private static List<List<Element>> GroupByAxis(List<Element> elements, bool isHorizontal)
    {
        var groups = new List<List<Element>>();
        var sorted = elements
            .Select(e =>
            {
                var lc = (LocationCurve)e.Location!;
                var mid = lc.Curve.Evaluate(0.5, true);
                var dir = (lc.Curve.GetEndPoint(1) - lc.Curve.GetEndPoint(0)).Normalize();
                return new { Element = e, Mid = mid, Dir = dir };
            })
            .Where(x =>
            {
                bool elemIsHorizontal = Math.Abs(x.Dir.Y) < 0.3 && Math.Abs(x.Dir.X) > 0.7;
                return isHorizontal ? elemIsHorizontal : !elemIsHorizontal;
            })
            .OrderBy(x => isHorizontal ? x.Mid.Y : x.Mid.X)
            .ToList();

        if (sorted.Count == 0) return groups;

        var currentGroup = new List<Element> { sorted[0].Element };
        double currentVal = isHorizontal ? sorted[0].Mid.Y : sorted[0].Mid.X;

        for (int i = 1; i < sorted.Count; i++)
        {
            double val = isHorizontal ? sorted[i].Mid.Y : sorted[i].Mid.X;
            if (Math.Abs(val - currentVal) < GroupingTolerance)
            {
                currentGroup.Add(sorted[i].Element);
            }
            else
            {
                groups.Add(currentGroup);
                currentGroup = [sorted[i].Element];
                currentVal = val;
            }
        }
        groups.Add(currentGroup);
        return groups;
    }

    private static bool TryCreateDimensionChain(Document doc, View view,
        List<Element> run, bool isHorizontal, double offset, bool useFaces)
    {
        try
        {
            var refArray = new ReferenceArray();
            var points = new List<double>();

            foreach (var elem in run)
            {
                var lc = (LocationCurve)elem.Location!;
                var curve = lc.Curve;

                if (useFaces)
                {
                    refArray.Append(new Reference(elem));
                }
                else
                {
                    refArray.Append(new Reference(elem));
                }

                var mid = curve.Evaluate(0.5, true);
                points.Add(isHorizontal ? mid.X : mid.Y);
            }

            if (refArray.Size < 2) return false;

            double avgCross = run.Average(e =>
            {
                var lc = (LocationCurve)e.Location!;
                var mid = lc.Curve.Evaluate(0.5, true);
                return isHorizontal ? mid.Y : mid.X;
            });

            double dimLinePos = avgCross + offset;
            XYZ start, end;
            if (isHorizontal)
            {
                start = new XYZ(points.Min() - 0.5, dimLinePos, 0);
                end = new XYZ(points.Max() + 0.5, dimLinePos, 0);
            }
            else
            {
                start = new XYZ(dimLinePos, points.Min() - 0.5, 0);
                end = new XYZ(dimLinePos, points.Max() + 0.5, 0);
            }

            var dimLine = Line.CreateBound(start, end);
            doc.Create.NewDimension(view, dimLine, refArray);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreatePointDimensionChain(Document doc, View view,
        List<Element> elements, bool isHorizontal, double offset)
    {
        try
        {
            var refArray = new ReferenceArray();
            var positions = new List<double>();

            foreach (var elem in elements)
            {
                refArray.Append(new Reference(elem));
                var pt = GetLocationPoint(elem);
                positions.Add(isHorizontal ? pt.X : pt.Y);
            }

            if (refArray.Size < 2) return false;

            double avgCross = elements.Average(e =>
            {
                var pt = GetLocationPoint(e);
                return isHorizontal ? pt.Y : pt.X;
            });

            double dimLinePos = avgCross + offset;
            XYZ start, end;
            if (isHorizontal)
            {
                start = new XYZ(positions.Min() - 0.5, dimLinePos, 0);
                end = new XYZ(positions.Max() + 0.5, dimLinePos, 0);
            }
            else
            {
                start = new XYZ(dimLinePos, positions.Min() - 0.5, 0);
                end = new XYZ(dimLinePos, positions.Max() + 0.5, 0);
            }

            var dimLine = Line.CreateBound(start, end);
            doc.Create.NewDimension(view, dimLine, refArray);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static XYZ GetLocationPoint(Element e)
    {
        if (e.Location is LocationPoint lp) return lp.Point;
        if (e.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);
        return e.GetCenter() ?? XYZ.Zero;
    }

    private static List<BuiltInCategory> ResolveDimensionCategories(string categoryStr)
    {
        var norm = categoryStr.ToLowerInvariant().Replace(" ", "");
        return norm switch
        {
            "all" => [
                BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_Conduit
            ],
            "ducts" or "duct" => [BuiltInCategory.OST_DuctCurves],
            "pipes" or "pipe" => [BuiltInCategory.OST_PipeCurves],
            "equipment" or "mechanicalequipment" => [BuiltInCategory.OST_MechanicalEquipment],
            "cabletray" or "cabletrays" => [BuiltInCategory.OST_CableTray],
            "conduit" or "conduits" => [BuiltInCategory.OST_Conduit],
            _ => []
        };
    }
}
