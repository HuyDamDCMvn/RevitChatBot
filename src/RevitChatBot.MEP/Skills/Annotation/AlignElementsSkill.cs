using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Annotation;

[Skill("align_elements",
    "Align elements or annotations along an axis. " +
    "Supports: left, right, center (horizontal); top, bottom, middle (vertical). " +
    "Works with tags, text notes, viewports, equipment, and any Revit element.")]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs to align", isRequired: true)]
[SkillParameter("alignment", "string",
    "Alignment direction",
    isRequired: true,
    allowedValues: new[] { "left", "right", "center", "top", "bottom", "middle" })]
public class AlignElementsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var idsStr = parameters.GetValueOrDefault("element_ids")?.ToString();
        var alignment = parameters.GetValueOrDefault("alignment")?.ToString()?.ToLowerInvariant() ?? "left";

        if (string.IsNullOrWhiteSpace(idsStr))
            return SkillResult.Fail("element_ids is required.");

        var elementIds = ParseIds(idsStr);
        if (elementIds.Count < 2)
            return SkillResult.Fail("At least 2 element IDs are required for alignment.");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elements = elementIds
                .Select(id => document.GetElement(new ElementId(id)))
                .Where(e => e is not null)
                .ToList();

            if (elements.Count < 2)
                return new { success = false, message = "Less than 2 valid elements found.", moved = 0 };

            var positions = elements
                .Select(e => new { Element = e, Position = e!.GetAnnotationPosition() ?? e.GetCenter() })
                .Where(x => x.Position is not null)
                .ToList();

            if (positions.Count < 2)
                return new { success = false, message = "Could not determine positions for elements.", moved = 0 };

            double target = alignment switch
            {
                "left" => positions.Min(p => p.Position!.X),
                "right" => positions.Max(p => p.Position!.X),
                "center" => (positions.Min(p => p.Position!.X) + positions.Max(p => p.Position!.X)) / 2,
                "top" => positions.Max(p => p.Position!.Y),
                "bottom" => positions.Min(p => p.Position!.Y),
                "middle" => (positions.Min(p => p.Position!.Y) + positions.Max(p => p.Position!.Y)) / 2,
                _ => 0
            };

            bool isHorizontal = alignment is "left" or "right" or "center";

            using var tx = new Transaction(document, $"Align elements {alignment}");
            tx.Start();

            int movedCount = 0;
            foreach (var item in positions)
            {
                var pos = item.Position!;
                var newPos = isHorizontal
                    ? new XYZ(target, pos.Y, pos.Z)
                    : new XYZ(pos.X, target, pos.Z);

                if (pos.DistanceTo(newPos) < 1e-9) continue;

                if (item.Element!.SetAnnotationPosition(newPos))
                    movedCount++;
            }

            tx.Commit();
            return new { success = true, message = $"Aligned {movedCount} elements to {alignment}.", moved = movedCount };
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
