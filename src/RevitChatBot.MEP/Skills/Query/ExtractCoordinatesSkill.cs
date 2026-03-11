using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Query;

[Skill("extract_coordinates",
    "Extract XYZ coordinates and bounding box information for MEP elements. " +
    "Useful for coordination, spatial analysis, and clash investigation. " +
    "Inspired by DiStem Properties Extraction. Can also inject coordinates into parameters.")]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs. If empty, extracts for all elements in the given category.",
    isRequired: false)]
[SkillParameter("category", "string",
    "Category to extract from (used when element_ids is empty): " +
    "'ducts', 'pipes', 'equipment', 'electrical', 'plumbing'.",
    isRequired: false,
    allowedValues: new[] { "ducts", "pipes", "equipment", "electrical", "plumbing" })]
[SkillParameter("inject_to_params", "string",
    "'true' to write X,Y,Z values into shared parameters named 'ChatBot_X', 'ChatBot_Y', 'ChatBot_Z'. " +
    "Default 'false'.",
    isRequired: false)]
[SkillParameter("max_results", "integer",
    "Max elements to return. Default 50.",
    isRequired: false)]
public class ExtractCoordinatesSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ducts"] = BuiltInCategory.OST_DuctCurves,
        ["pipes"] = BuiltInCategory.OST_PipeCurves,
        ["equipment"] = BuiltInCategory.OST_MechanicalEquipment,
        ["electrical"] = BuiltInCategory.OST_ElectricalEquipment,
        ["plumbing"] = BuiltInCategory.OST_PlumbingFixtures,
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var idsStr = parameters.GetValueOrDefault("element_ids")?.ToString();
        var category = parameters.GetValueOrDefault("category")?.ToString();
        var inject = parameters.GetValueOrDefault("inject_to_params")?.ToString()?.ToLower() == "true";
        var maxResults = 50;
        if (parameters.TryGetValue("max_results", out var mr) && mr is not null)
            int.TryParse(mr.ToString(), out maxResults);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elements = ResolveElements(document, idsStr, category, maxResults);

            if (elements.Count == 0)
                return new { total = 0, elements = new List<object>(), injected = false };

            var coordData = new List<object>();
            foreach (var elem in elements)
            {
                var bb = elem.get_BoundingBox(null);
                var location = elem.Location;

                XYZ? point = null;
                XYZ? startPoint = null;
                XYZ? endPoint = null;

                if (location is LocationPoint lp)
                    point = lp.Point;
                else if (location is LocationCurve lc)
                {
                    startPoint = lc.Curve.GetEndPoint(0);
                    endPoint = lc.Curve.GetEndPoint(1);
                    point = (startPoint + endPoint) / 2;
                }
                else if (bb is not null)
                    point = (bb.Min + bb.Max) / 2;

                coordData.Add(new
                {
                    id = elem.Id.Value,
                    name = elem.Name,
                    category = elem.Category?.Name ?? "",
                    center = point is not null ? FormatPoint(point) : null,
                    start = startPoint is not null ? FormatPoint(startPoint) : null,
                    end = endPoint is not null ? FormatPoint(endPoint) : null,
                    boundingBox = bb is not null ? new
                    {
                        min = FormatPoint(bb.Min),
                        max = FormatPoint(bb.Max),
                        size = new
                        {
                            width = Math.Round(FeetToMm(bb.Max.X - bb.Min.X), 1),
                            depth = Math.Round(FeetToMm(bb.Max.Y - bb.Min.Y), 1),
                            height = Math.Round(FeetToMm(bb.Max.Z - bb.Min.Z), 1)
                        }
                    } : null,
                    level = GetLevel(document, elem)
                });
            }

            var injected = false;
            if (inject)
            {
                injected = InjectCoordinates(document, elements);
            }

            return new
            {
                total = coordData.Count,
                elements = coordData,
                injected
            };
        });

        dynamic res = result!;
        var msg = $"Extracted coordinates for {res.total} elements.";
        if (inject) msg += res.injected ? " Coordinates injected into parameters." : " Injection failed (parameters may not exist).";
        return SkillResult.Ok(msg, result);
    }

    private static List<Element> ResolveElements(Document doc, string? idsStr, string? category, int max)
    {
        if (!string.IsNullOrWhiteSpace(idsStr))
        {
            return idsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => long.TryParse(s, out _))
                .Select(s => doc.GetElement(new ElementId(long.Parse(s))))
                .Where(e => e is not null)
                .Take(max)
                .ToList()!;
        }

        if (!string.IsNullOrWhiteSpace(category) && CategoryMap.TryGetValue(category, out var bic))
        {
            return new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .Take(max)
                .ToList();
        }

        return [];
    }

    private static object FormatPoint(XYZ pt) => new
    {
        x_mm = Math.Round(FeetToMm(pt.X), 1),
        y_mm = Math.Round(FeetToMm(pt.Y), 1),
        z_mm = Math.Round(FeetToMm(pt.Z), 1),
        x_ft = Math.Round(pt.X, 4),
        y_ft = Math.Round(pt.Y, 4),
        z_ft = Math.Round(pt.Z, 4)
    };

    private static double FeetToMm(double feet) => feet * 304.8;

    private static bool InjectCoordinates(Document doc, List<Element> elements)
    {
        try
        {
            using var tx = new Transaction(doc, "Inject coordinates");
            tx.Start();

            foreach (var elem in elements)
            {
                var bb = elem.get_BoundingBox(null);
                var loc = elem.Location;
                XYZ? pt = null;

                if (loc is LocationPoint lp) pt = lp.Point;
                else if (loc is LocationCurve lc) pt = (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) / 2;
                else if (bb is not null) pt = (bb.Min + bb.Max) / 2;

                if (pt is null) continue;

                SetParamIfExists(elem, "ChatBot_X", FeetToMm(pt.X).ToString("F1"));
                SetParamIfExists(elem, "ChatBot_Y", FeetToMm(pt.Y).ToString("F1"));
                SetParamIfExists(elem, "ChatBot_Z", FeetToMm(pt.Z).ToString("F1"));
            }

            tx.Commit();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SetParamIfExists(Element elem, string paramName, string value)
    {
        var param = elem.LookupParameter(paramName);
        if (param is null || param.IsReadOnly) return;

        if (param.StorageType == StorageType.String)
            param.Set(value);
        else if (param.StorageType == StorageType.Double && double.TryParse(value, out var d))
            param.Set(d);
    }

    private static string GetLevel(Document doc, Element elem)
    {
        if (elem.LevelId is { } lid && lid != ElementId.InvalidElementId)
            return doc.GetElement(lid)?.Name ?? "";
        return elem.LookupParameter("Reference Level")?.AsValueString()
               ?? elem.LookupParameter("Level")?.AsValueString()
               ?? "";
    }
}
