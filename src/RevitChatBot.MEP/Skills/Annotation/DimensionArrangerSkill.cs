using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Annotation;

[Skill("arrange_dimensions",
    "Arrange existing dimension lines in a view for cleaner presentation. " +
    "Groups parallel dimensions and spaces them evenly. " +
    "Moves dimension lines to avoid overlapping with model elements and tags.")]
[SkillParameter("view_id", "string",
    "Element ID of the target view", isRequired: true)]
[SkillParameter("spacing", "string",
    "Spacing between parallel dimension chains in feet (default '0.3')",
    isRequired: false)]
public class DimensionArrangerSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var viewIdStr = parameters.GetValueOrDefault("view_id")?.ToString();
        var spacingStr = parameters.GetValueOrDefault("spacing")?.ToString();

        if (string.IsNullOrWhiteSpace(viewIdStr) || !long.TryParse(viewIdStr, out var viewIdLong))
            return SkillResult.Fail("view_id is required and must be a valid element ID.");

        double spacing = 0.3;
        if (!string.IsNullOrEmpty(spacingStr) && double.TryParse(spacingStr, out var parsed))
            spacing = Math.Max(0.1, parsed);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var viewElem = document.GetElement(new ElementId(viewIdLong));
            if (viewElem is not View view)
                return new { success = false, message = "Invalid view ID.", moved = 0 };

            var dims = new FluentCollector(document)
                .OfDimensions()
                .WhereElementIsNotElementType()
                .InView(view.Id)
                .ToList<Dimension>()
                .Where(d => d.Curve is Line)
                .ToList();

            if (dims.Count < 2)
                return new { success = true, message = $"Only {dims.Count} dimension(s) found.", moved = 0 };

            var horizontal = new List<Dimension>();
            var vertical = new List<Dimension>();

            foreach (var dim in dims)
            {
                var line = (Line)dim.Curve!;
                var dir = line.Direction.Normalize();
                if (Math.Abs(dir.X) > Math.Abs(dir.Y))
                    horizontal.Add(dim);
                else
                    vertical.Add(dim);
            }

            using var tx = new Transaction(document, "Arrange dimensions");
            tx.Start();

            int movedCount = 0;
            movedCount += ArrangeGroup(horizontal, isHorizontal: true, spacing, view);
            movedCount += ArrangeGroup(vertical, isHorizontal: false, spacing, view);

            tx.Commit();

            return new
            {
                success = true,
                message = $"Arranged {dims.Count} dimensions ({horizontal.Count} horizontal, " +
                          $"{vertical.Count} vertical). Moved {movedCount} dimension lines.",
                moved = movedCount
            };
        });

        var r = (dynamic)result!;
        return (bool)r.success
            ? SkillResult.Ok((string)r.message, result)
            : SkillResult.Fail((string)r.message);
    }

    private static int ArrangeGroup(List<Dimension> dims, bool isHorizontal, double spacing, View view)
    {
        if (dims.Count < 2) return 0;

        var sorted = dims
            .Select(d =>
            {
                var line = (Line)d.Curve!;
                var mid = line.Evaluate(0.5, true);
                return new { Dim = d, CrossPos = isHorizontal ? mid.Y : mid.X, Mid = mid };
            })
            .OrderBy(x => x.CrossPos)
            .ToList();

        int moved = 0;
        double basePos = sorted[0].CrossPos;

        for (int i = 1; i < sorted.Count; i++)
        {
            double targetPos = basePos + spacing * i;
            double currentPos = sorted[i].CrossPos;
            double delta = targetPos - currentPos;

            if (Math.Abs(delta) < 0.01) continue;

            try
            {
                var translation = isHorizontal
                    ? new XYZ(0, delta, 0)
                    : new XYZ(delta, 0, 0);
                ElementTransformUtils.MoveElement(
                    sorted[i].Dim.Document, sorted[i].Dim.Id, translation);
                moved++;
            }
            catch { }
        }

        return moved;
    }
}
