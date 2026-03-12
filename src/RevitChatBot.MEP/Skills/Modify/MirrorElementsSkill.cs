using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("mirror_elements",
    "Mirror elements across an axis or plane. Supports mirroring across a grid line, " +
    "reference plane, or a custom axis defined by two points. " +
    "Creates mirrored copies (default) or moves the original elements.")]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs to mirror.",
    isRequired: true)]
[SkillParameter("axis", "string",
    "Mirror axis: 'x' (mirror across Y-axis at origin), 'y' (mirror across X-axis at origin), " +
    "or a grid/reference plane name. Default: 'x'.",
    isRequired: false)]
[SkillParameter("copy", "string",
    "'true' to create mirrored copies (keep original), 'false' to move original. Default: 'true'.",
    isRequired: false)]
[SkillParameter("origin_x_mm", "number",
    "X coordinate of axis origin in mm. Default: 0.",
    isRequired: false)]
[SkillParameter("origin_y_mm", "number",
    "Y coordinate of axis origin in mm. Default: 0.",
    isRequired: false)]
public class MirrorElementsSkill : ISkill
{
    private const double MmToFeet = 1.0 / 304.8;

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var idsStr = parameters.GetValueOrDefault("element_ids")?.ToString();
        if (string.IsNullOrWhiteSpace(idsStr))
            return SkillResult.Fail("'element_ids' is required.");

        var axisParam = parameters.GetValueOrDefault("axis")?.ToString()?.Trim().ToLower() ?? "x";
        var copy = parameters.GetValueOrDefault("copy")?.ToString()?.ToLower() != "false";
        var originX = Convert.ToDouble(parameters.GetValueOrDefault("origin_x_mm") ?? 0) * MmToFeet;
        var originY = Convert.ToDouble(parameters.GetValueOrDefault("origin_y_mm") ?? 0) * MmToFeet;

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elementIds = idsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => new ElementId(long.Parse(s.Trim())))
                .Where(id => document.GetElement(id) is not null)
                .ToList();

            if (elementIds.Count == 0)
                return new { status = "error", message = "No valid elements found." };

            Plane? mirrorPlane = ResolveMirrorPlane(document, axisParam, originX, originY);
            if (mirrorPlane is null)
                return new { status = "error", message = $"Could not resolve mirror axis '{axisParam}'." };

            using var tx = new Transaction(document, "Mirror elements");
            tx.Start();
            try
            {
                var idCollection = elementIds.ToList();
                if (copy)
                {
                    ElementTransformUtils.MirrorElements(
                        document, idCollection, mirrorPlane, true);
                }
                else
                {
                    ElementTransformUtils.MirrorElements(
                        document, idCollection, mirrorPlane, false);
                }
                tx.Commit();

                return new
                {
                    status = "ok",
                    mirrored = elementIds.Count,
                    copy,
                    axis = axisParam,
                    message = $"Mirrored {elementIds.Count} elements across '{axisParam}'. Copy={copy}."
                };
            }
            catch (Exception ex)
            {
                if (tx.HasStarted()) tx.RollBack();
                return new { status = "error", message = ex.Message };
            }
        });

        dynamic res = result!;
        if (res.status == "ok")
            return SkillResult.Ok(res.message, result);
        return SkillResult.Fail(res.message);
    }

    private static Plane? ResolveMirrorPlane(Document doc, string axis, double originX, double originY)
    {
        if (axis == "x")
            return Plane.CreateByNormalAndOrigin(XYZ.BasisX, new XYZ(originX, originY, 0));
        if (axis == "y")
            return Plane.CreateByNormalAndOrigin(XYZ.BasisY, new XYZ(originX, originY, 0));

        var grid = new FilteredElementCollector(doc)
            .OfClass(typeof(Grid))
            .Cast<Grid>()
            .FirstOrDefault(g => g.Name.Equals(axis, StringComparison.OrdinalIgnoreCase));

        if (grid is not null)
        {
            var curve = grid.Curve;
            var start = curve.GetEndPoint(0);
            var dir = (curve.GetEndPoint(1) - start).Normalize();
            var normal = dir.CrossProduct(XYZ.BasisZ).Normalize();
            return Plane.CreateByNormalAndOrigin(normal, start);
        }

        var refPlane = new FilteredElementCollector(doc)
            .OfClass(typeof(ReferencePlane))
            .Cast<ReferencePlane>()
            .FirstOrDefault(rp => rp.Name.Equals(axis, StringComparison.OrdinalIgnoreCase));

        if (refPlane is not null)
        {
            return refPlane.GetPlane();
        }

        return null;
    }
}
