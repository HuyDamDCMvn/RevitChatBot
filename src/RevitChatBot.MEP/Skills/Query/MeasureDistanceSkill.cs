using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Query;

/// <summary>
/// Measure distance between two elements or between an element and a reference (level, grid).
/// Returns distance in both mm and feet. Useful as a building block for coordination checks
/// and spatial queries that codegen can chain.
/// </summary>
[Skill("measure_distance",
    "Measure distance between two elements by their bounding box centers. " +
    "Returns total 3D distance, horizontal distance, and vertical distance in mm. " +
    "Use for spacing verification, coordination checks, and spatial queries.")]
[SkillParameter("element_id_a", "string",
    "First element ID.",
    isRequired: true)]
[SkillParameter("element_id_b", "string",
    "Second element ID.",
    isRequired: true)]
[SkillParameter("mode", "string",
    "Distance mode: '3d' (full 3D distance), 'horizontal' (XY plane only), " +
    "'vertical' (Z axis only). Default: '3d'.",
    isRequired: false, allowedValues: new[] { "3d", "horizontal", "vertical" })]
public class MeasureDistanceSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var idAStr = parameters.GetValueOrDefault("element_id_a")?.ToString();
        var idBStr = parameters.GetValueOrDefault("element_id_b")?.ToString();
        if (string.IsNullOrWhiteSpace(idAStr) || string.IsNullOrWhiteSpace(idBStr))
            return SkillResult.Fail("Both 'element_id_a' and 'element_id_b' are required.");

        if (!long.TryParse(idAStr, out var rawA) || !long.TryParse(idBStr, out var rawB))
            return SkillResult.Fail("Invalid element IDs. Must be numeric.");

        var mode = parameters.GetValueOrDefault("mode")?.ToString()?.ToLowerInvariant() ?? "3d";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elemA = document.GetElement(new ElementId(rawA));
            var elemB = document.GetElement(new ElementId(rawB));

            if (elemA is null) return new { error = $"Element {rawA} not found." };
            if (elemB is null) return new { error = $"Element {rawB} not found." };

            var centerA = GetCenter(elemA);
            var centerB = GetCenter(elemB);
            if (centerA is null || centerB is null)
                return new { error = "Cannot determine element positions (no bounding box or location)." };

            var dx = centerB.X - centerA.X;
            var dy = centerB.Y - centerA.Y;
            var dz = centerB.Z - centerA.Z;

            var distHorizontalFt = Math.Sqrt(dx * dx + dy * dy);
            var distVerticalFt = Math.Abs(dz);
            var dist3dFt = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            return new
            {
                elementA = new { id = rawA, name = elemA.Name, category = elemA.Category?.Name },
                elementB = new { id = rawB, name = elemB.Name, category = elemB.Category?.Name },
                distance3d_mm = Math.Round(dist3dFt * 304.8, 1),
                distanceHorizontal_mm = Math.Round(distHorizontalFt * 304.8, 1),
                distanceVertical_mm = Math.Round(distVerticalFt * 304.8, 1),
                delta = new
                {
                    x_mm = Math.Round(dx * 304.8, 1),
                    y_mm = Math.Round(dy * 304.8, 1),
                    z_mm = Math.Round(dz * 304.8, 1)
                }
            };
        });

        dynamic res = result!;
        if (((IDictionary<string, object>)res).ContainsKey("error"))
            return SkillResult.Fail(res.error?.ToString() ?? "Measurement failed.");

        return SkillResult.Ok($"Distance measured: {res.distance3d_mm}mm (3D).", result);
    }

    private static XYZ? GetCenter(Element elem)
    {
        if (elem.Location is LocationPoint lp) return lp.Point;
        if (elem.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);

        var bb = elem.get_BoundingBox(null);
        if (bb is null) return null;
        return (bb.Min + bb.Max) / 2.0;
    }
}
