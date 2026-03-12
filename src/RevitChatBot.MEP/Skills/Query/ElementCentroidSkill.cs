using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Query;

/// <summary>
/// Calculates the geometric centroid (bounding box center) of selected or specified elements.
/// </summary>
[Skill("get_element_centroid",
    "Calculate the centroid (center point) of one or more elements based on their bounding boxes. " +
    "Returns individual centroids and the combined centroid of the group.")]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs. If omitted, uses current selection.",
    isRequired: false)]
[SkillParameter("source", "string",
    "'selected' to use current selection. Default 'selected'.",
    isRequired: false, allowedValues: new[] { "selected", "ids" })]
public class ElementCentroidSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var source = parameters.GetValueOrDefault("source")?.ToString() ?? "selected";
        var idsStr = parameters.GetValueOrDefault("element_ids")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            List<long> ids;

            if (!string.IsNullOrWhiteSpace(idsStr))
            {
                ids = idsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => long.TryParse(s, out var v) ? v : -1)
                    .Where(v => v >= 0)
                    .ToList();
            }
            else
            {
                ids = context.GetCurrentSelectionIds() ?? [];
            }

            if (ids.Count == 0)
                return new { error = "No elements specified or selected.", centroids = Array.Empty<object>(), combined = (object?)null };

            var centroids = new List<object>();
            double sumX = 0, sumY = 0, sumZ = 0;
            int validCount = 0;

            foreach (var id in ids)
            {
                var elem = document.GetElement(new ElementId(id));
                if (elem is null) continue;

                var bb = elem.get_BoundingBox(null);
                if (bb is null) continue;

                var cx = (bb.Min.X + bb.Max.X) / 2.0;
                var cy = (bb.Min.Y + bb.Max.Y) / 2.0;
                var cz = (bb.Min.Z + bb.Max.Z) / 2.0;

                sumX += cx; sumY += cy; sumZ += cz;
                validCount++;

                centroids.Add(new
                {
                    id,
                    name = elem.Name,
                    x_feet = Math.Round(cx, 4),
                    y_feet = Math.Round(cy, 4),
                    z_feet = Math.Round(cz, 4),
                    x_mm = Math.Round(cx * 304.8, 1),
                    y_mm = Math.Round(cy * 304.8, 1),
                    z_mm = Math.Round(cz * 304.8, 1)
                });
            }

            object? combined = validCount > 0
                ? new
                {
                    x_feet = Math.Round(sumX / validCount, 4),
                    y_feet = Math.Round(sumY / validCount, 4),
                    z_feet = Math.Round(sumZ / validCount, 4),
                    x_mm = Math.Round(sumX / validCount * 304.8, 1),
                    y_mm = Math.Round(sumY / validCount * 304.8, 1),
                    z_mm = Math.Round(sumZ / validCount * 304.8, 1)
                }
                : null;

            return new { error = (string?)null, centroids, combined, count = validCount };
        });

        var data = result as dynamic;
        if (data?.error is string err && !string.IsNullOrEmpty(err))
            return SkillResult.Fail(err);

        return SkillResult.Ok($"Centroid calculated for {data?.count} elements.", result);
    }
}
