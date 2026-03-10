using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("split_duct_pipe",
    "Split ducts or pipes into equal segments at a specified distance. Automatically creates " +
    "union fittings between adjacent segments and numbers each segment sequentially into a " +
    "chosen parameter. Supports splitting from start-to-end or end-to-start direction.")]
[SkillParameter("category", "string",
    "Element type to split: duct or pipe",
    isRequired: true, allowedValues: ["duct", "pipe"])]
[SkillParameter("split_distance_mm", "number",
    "Split distance in mm (e.g., 1000 = 1 meter segments)", isRequired: true)]
[SkillParameter("direction", "string",
    "Split direction: start_to_end or end_to_start. Default: start_to_end",
    isRequired: false, allowedValues: ["start_to_end", "end_to_start"])]
[SkillParameter("number_param", "string",
    "Parameter name to store segment order number (e.g., 'Comments', 'Mark'). Default: Comments",
    isRequired: false)]
[SkillParameter("level_name", "string",
    "Filter elements by level name (optional, empty = all levels)", isRequired: false)]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs to split (optional, empty = all on level/view)", isRequired: false)]
public class SplitDuctPipeSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        string category = (parameters.GetValueOrDefault("category") as string)?.ToLowerInvariant() ?? "duct";
        double splitMm = ParseDouble(parameters.GetValueOrDefault("split_distance_mm"), 0);
        string direction = (parameters.GetValueOrDefault("direction") as string)?.ToLowerInvariant() ?? "start_to_end";
        string numberParam = parameters.GetValueOrDefault("number_param") as string ?? "Comments";
        string? levelName = parameters.GetValueOrDefault("level_name") as string;
        string? elementIdsCsv = parameters.GetValueOrDefault("element_ids") as string;

        if (splitMm <= 0)
            return SkillResult.Fail("split_distance_mm must be greater than 0.");

        double splitFt = splitMm / 304.8;
        bool fromStart = direction == "start_to_end";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elements = CollectElements(document, category, levelName, elementIdsCsv, splitFt);

            if (elements.Count == 0)
                return new { error = $"No {category} elements found (or all shorter than {splitMm}mm)." };

            using var tx = new Transaction(document, $"Split {category} at {splitMm}mm");
            tx.Start();

            int totalSegments = 0;
            int totalFittings = 0;
            int failedElements = 0;
            var details = new List<object>();

            foreach (var elem in elements)
            {
                try
                {
                    var splitResult = SplitElement(document, elem, category, splitFt, fromStart, numberParam);
                    totalSegments += splitResult.Segments;
                    totalFittings += splitResult.Fittings;

                    details.Add(new
                    {
                        originalElementId = elem.Id.Value,
                        segments = splitResult.Segments,
                        fittings = splitResult.Fittings,
                        success = true
                    });
                }
                catch (Exception ex)
                {
                    failedElements++;
                    details.Add(new
                    {
                        originalElementId = elem.Id.Value,
                        segments = 0,
                        fittings = 0,
                        success = false,
                        error = ex.Message
                    });
                }
            }

            if (totalSegments > 0)
                tx.Commit();
            else
                tx.RollBack();

            return new
            {
                category,
                splitDistanceMm = splitMm,
                direction,
                numberParameter = numberParam,
                totalElementsProcessed = elements.Count,
                totalSegmentsCreated = totalSegments,
                totalFittingsCreated = totalFittings,
                failedElements,
                details = details.Take(50).ToList()
            };
        });

        if (result is IDictionary<string, object> dict && dict.ContainsKey("error"))
            return SkillResult.Fail(dict["error"]?.ToString() ?? "Error");

        return SkillResult.Ok("Split operation completed.", result);
    }

    private static List<Element> CollectElements(
        Document doc, string category, string? levelName, string? elementIdsCsv, double minLengthFt)
    {
        if (!string.IsNullOrWhiteSpace(elementIdsCsv))
        {
            return elementIdsCsv.Split(',')
                .Select(s => s.Trim())
                .Where(s => long.TryParse(s, out _))
                .Select(s => doc.GetElement(new ElementId(long.Parse(s))))
                .Where(e => e is not null && IsCurveElement(e) && GetCurveLength(e) > minLengthFt)
                .ToList()!;
        }

        var bic = category == "pipe"
            ? BuiltInCategory.OST_PipeCurves
            : BuiltInCategory.OST_DuctCurves;

        var elements = new FilteredElementCollector(doc)
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .Where(e => IsCurveElement(e) && GetCurveLength(e) > minLengthFt)
            .ToList();

        if (!string.IsNullOrEmpty(levelName))
        {
            elements = elements.Where(e =>
            {
                var lvlParam = e.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                if (lvlParam?.AsElementId() is not { } id || id == ElementId.InvalidElementId)
                    return false;
                return doc.GetElement(id)?.Name?.Equals(levelName, StringComparison.OrdinalIgnoreCase) == true;
            }).ToList();
        }

        return elements;
    }

    private static SplitResult SplitElement(
        Document doc, Element elem, string category, double splitFt, bool fromStart, string numberParam)
    {
        var curve = ((LocationCurve)elem.Location).Curve;
        double totalLength = curve.Length;
        int numSplits = (int)(totalLength / splitFt);
        if (numSplits <= 0) return new SplitResult(1, 0);

        var splitPoints = new List<XYZ>();
        for (int i = 1; i <= numSplits; i++)
        {
            double dist = splitFt * i;
            if (dist >= totalLength - doc.Application.ShortCurveTolerance) break;

            XYZ pt = fromStart
                ? ComputePointFromStart(curve, dist)
                : ComputePointFromEnd(curve, dist);
            splitPoints.Add(pt);
        }

        if (splitPoints.Count == 0) return new SplitResult(1, 0);

        bool isDuct = category == "duct";
        var segmentsAfterSplit = new List<ElementId>();
        var currentElemId = elem.Id;

        foreach (var pt in splitPoints)
        {
            ElementId newId;
            if (isDuct)
                newId = MechanicalUtils.BreakCurve(doc, currentElemId, pt);
            else
                newId = PlumbingUtils.BreakCurve(doc, currentElemId, pt);

            if (fromStart)
            {
                segmentsAfterSplit.Add(newId);
            }
            else
            {
                segmentsAfterSplit.Add(currentElemId);
                currentElemId = newId;
            }
        }

        if (fromStart)
            segmentsAfterSplit.Add(currentElemId);
        else
            segmentsAfterSplit.Insert(0, currentElemId);

        for (int idx = 0; idx < segmentsAfterSplit.Count; idx++)
        {
            var segElem = doc.GetElement(segmentsAfterSplit[idx]);
            if (segElem is null) continue;

            var param = segElem.LookupParameter(numberParam);
            if (param is null || param.IsReadOnly) continue;

            int order = idx + 1;
            try
            {
                if (param.StorageType == StorageType.String)
                    param.Set(order.ToString());
                else if (param.StorageType == StorageType.Integer)
                    param.Set(order);
                else if (param.StorageType == StorageType.Double)
                    param.Set((double)order);
            }
            catch { /* ignore param set failures */ }
        }

        int fittings = 0;
        for (int i = 0; i < segmentsAfterSplit.Count - 1; i++)
        {
            try
            {
                var fitting = CreateUnionFitting(doc, segmentsAfterSplit[i], segmentsAfterSplit[i + 1]);
                if (fitting is not null) fittings++;
            }
            catch { /* fitting creation can fail if routing prefs missing */ }
        }

        return new SplitResult(segmentsAfterSplit.Count, fittings);
    }

    private static FamilyInstance? CreateUnionFitting(Document doc, ElementId id1, ElementId id2)
    {
        var e1 = doc.GetElement(id1);
        var e2 = doc.GetElement(id2);
        if (e1 is not MEPCurve mc1 || e2 is not MEPCurve mc2) return null;

        foreach (Connector c1 in mc1.ConnectorManager.Connectors)
        {
            foreach (Connector c2 in mc2.ConnectorManager.Connectors)
            {
                if (c1.Origin.DistanceTo(c2.Origin) < 0.001)
                {
                    return doc.Create.NewUnionFitting(c1, c2);
                }
            }
        }
        return null;
    }

    private static XYZ ComputePointFromStart(Curve curve, double distance)
    {
        var start = curve.GetEndPoint(0);
        var end = curve.GetEndPoint(1);
        var dir = end - start;
        double len = dir.GetLength();
        if (len < 1e-9) return start;
        double ratio = distance / len;
        return start + dir * ratio;
    }

    private static XYZ ComputePointFromEnd(Curve curve, double distance)
    {
        var start = curve.GetEndPoint(0);
        var end = curve.GetEndPoint(1);
        var dir = start - end;
        double len = dir.GetLength();
        if (len < 1e-9) return end;
        double ratio = distance / len;
        return end + dir * ratio;
    }

    private static bool IsCurveElement(Element e) => e.Location is LocationCurve;

    private static double GetCurveLength(Element e)
    {
        if (e.Location is LocationCurve lc)
            return lc.Curve.Length;
        return 0;
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is long l) return l;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }

    private record struct SplitResult(int Segments, int Fittings);
}
