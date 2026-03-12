using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("rotate_elements",
    "Rotate elements around a vertical axis at their center, or at a specified point. " +
    "Common use: rotate equipment, rotate fittings, adjust orientation of placed families.")]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs to rotate.",
    isRequired: true)]
[SkillParameter("angle_degrees", "number",
    "Rotation angle in degrees. Positive = counter-clockwise, negative = clockwise.",
    isRequired: true)]
[SkillParameter("center_mode", "string",
    "Rotation center: 'element' (each element rotates around its own center), " +
    "'point' (all rotate around a specified point). Default: 'element'.",
    isRequired: false, allowedValues: new[] { "element", "point" })]
[SkillParameter("center_x_mm", "number",
    "X coordinate of rotation center in mm (when center_mode='point').",
    isRequired: false)]
[SkillParameter("center_y_mm", "number",
    "Y coordinate of rotation center in mm (when center_mode='point').",
    isRequired: false)]
public class RotateElementsSkill : ISkill
{
    private const double MmToFeet = 1.0 / 304.8;
    private const double DegToRad = Math.PI / 180.0;

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

        if (!parameters.TryGetValue("angle_degrees", out var angleObj) || angleObj is null)
            return SkillResult.Fail("'angle_degrees' is required.");

        var angleDeg = Convert.ToDouble(angleObj);
        var angleRad = angleDeg * DegToRad;

        var centerMode = parameters.GetValueOrDefault("center_mode")?.ToString()?.ToLower() ?? "element";
        var centerX = Convert.ToDouble(parameters.GetValueOrDefault("center_x_mm") ?? 0) * MmToFeet;
        var centerY = Convert.ToDouble(parameters.GetValueOrDefault("center_y_mm") ?? 0) * MmToFeet;

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elementIds = idsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => new ElementId(long.Parse(s.Trim())))
                .Where(id => document.GetElement(id) is not null)
                .ToList();

            if (elementIds.Count == 0)
                return new { status = "error", message = "No valid elements found.", rotated = 0 };

            using var tx = new Transaction(document, "Rotate elements");
            tx.Start();
            try
            {
                int rotated = 0;
                foreach (var eid in elementIds)
                {
                    var elem = document.GetElement(eid);
                    if (elem is null) continue;

                    XYZ center;
                    if (centerMode == "point")
                    {
                        var bb = elem.get_BoundingBox(null);
                        var z = bb is not null ? (bb.Min.Z + bb.Max.Z) / 2.0 : 0;
                        center = new XYZ(centerX, centerY, z);
                    }
                    else
                    {
                        var bb = elem.get_BoundingBox(null);
                        if (bb is null) continue;
                        center = (bb.Min + bb.Max) / 2.0;
                    }

                    var axis = Line.CreateBound(center, center + XYZ.BasisZ * 10);
                    ElementTransformUtils.RotateElement(document, eid, axis, angleRad);
                    rotated++;
                }

                tx.Commit();
                return new
                {
                    status = "ok",
                    message = $"Rotated {rotated} elements by {angleDeg}°.",
                    rotated
                };
            }
            catch (Exception ex)
            {
                if (tx.HasStarted()) tx.RollBack();
                return new { status = "error", message = ex.Message, rotated = 0 };
            }
        });

        dynamic res = result!;
        return res.status == "ok"
            ? SkillResult.Ok(res.message, result)
            : SkillResult.Fail(res.message);
    }
}
