using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Annotation;

[Skill("distribute_elements",
    "Distribute elements or annotations evenly with equal spacing. " +
    "Keeps the first and last elements in place, redistributes elements in between. " +
    "Works with tags, text notes, viewports, equipment, and any Revit element.")]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs to distribute", isRequired: true)]
[SkillParameter("direction", "string",
    "Distribution direction",
    isRequired: true,
    allowedValues: new[] { "horizontal", "vertical" })]
public class DistributeElementsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var idsStr = parameters.GetValueOrDefault("element_ids")?.ToString();
        var direction = parameters.GetValueOrDefault("direction")?.ToString()?.ToLowerInvariant() ?? "horizontal";

        if (string.IsNullOrWhiteSpace(idsStr))
            return SkillResult.Fail("element_ids is required.");

        var elementIds = ParseIds(idsStr);
        if (elementIds.Count < 3)
            return SkillResult.Fail("At least 3 element IDs are required for distribution.");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            bool isHorizontal = direction == "horizontal";

            var items = elementIds
                .Select(id => document.GetElement(new ElementId(id)))
                .Where(e => e is not null)
                .Select(e => new
                {
                    Element = e!,
                    Position = e!.GetAnnotationPosition() ?? e.GetCenter()
                })
                .Where(x => x.Position is not null)
                .OrderBy(x => isHorizontal ? x.Position!.X : x.Position!.Y)
                .ToList();

            if (items.Count < 3)
                return new { success = false, message = "Less than 3 valid elements found.", moved = 0 };

            double first = isHorizontal ? items.First().Position!.X : items.First().Position!.Y;
            double last = isHorizontal ? items.Last().Position!.X : items.Last().Position!.Y;
            double step = (last - first) / (items.Count - 1);

            using var tx = new Transaction(document, $"Distribute elements {direction}");
            tx.Start();

            int movedCount = 0;
            for (int i = 1; i < items.Count - 1; i++)
            {
                var pos = items[i].Position!;
                double targetVal = first + step * i;

                var newPos = isHorizontal
                    ? new XYZ(targetVal, pos.Y, pos.Z)
                    : new XYZ(pos.X, targetVal, pos.Z);

                if (pos.DistanceTo(newPos) < 1e-9) continue;

                if (items[i].Element.SetAnnotationPosition(newPos))
                    movedCount++;
            }

            tx.Commit();
            return new
            {
                success = true,
                message = $"Distributed {items.Count} elements {direction}ly. Moved {movedCount} inner elements.",
                moved = movedCount
            };
        });

        var r = (dynamic)result!;
        return (bool)r.success
            ? SkillResult.Ok((string)r.message, result)
            : SkillResult.Fail((string)r.message);
    }

    private static List<long> ParseIds(string idsStr)
    {
        return idsStr
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => long.TryParse(s, out _))
            .Select(long.Parse)
            .ToList();
    }
}
