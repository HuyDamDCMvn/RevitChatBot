using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Annotation;

[Skill("arrange_viewports",
    "Arrange viewports on a sheet with even spacing in a grid, row, or column layout. " +
    "Automatically calculates optimal positions within the sheet's title block area.")]
[SkillParameter("sheet_id", "string",
    "Element ID of the target sheet (ViewSheet)", isRequired: true)]
[SkillParameter("layout", "string",
    "Layout arrangement: 'grid' (auto rows/cols), 'horizontal' (single row), 'vertical' (single column)",
    isRequired: false,
    allowedValues: new[] { "grid", "horizontal", "vertical" })]
[SkillParameter("margin", "string",
    "Margin from sheet edges in feet (default 0.1 = ~1.2 inches)",
    isRequired: false)]
public class ArrangeViewportsSkill : ISkill
{
    private const double DefaultMargin = 0.1; // ~1.2 inches

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var sheetIdStr = parameters.GetValueOrDefault("sheet_id")?.ToString();
        var layout = parameters.GetValueOrDefault("layout")?.ToString()?.ToLowerInvariant() ?? "grid";
        var marginStr = parameters.GetValueOrDefault("margin")?.ToString();

        if (string.IsNullOrWhiteSpace(sheetIdStr) || !long.TryParse(sheetIdStr, out var sheetIdLong))
            return SkillResult.Fail("sheet_id is required and must be a valid element ID.");

        double margin = DefaultMargin;
        if (!string.IsNullOrEmpty(marginStr) && double.TryParse(marginStr, out var parsedMargin))
            margin = Math.Max(0.01, parsedMargin);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var sheetElem = document.GetElement(new ElementId(sheetIdLong));
            if (sheetElem is not ViewSheet sheet)
                return new { success = false, message = "Invalid sheet ID or element is not a ViewSheet.", moved = 0 };

            var viewportIds = sheet.GetAllViewports();
            if (viewportIds.Count == 0)
                return new { success = true, message = "No viewports on this sheet.", moved = 0 };

            var viewports = viewportIds
                .Select(id => document.GetElement(id))
                .OfType<Viewport>()
                .ToList();

            if (viewports.Count == 0)
                return new { success = true, message = "No viewports found.", moved = 0 };

            var sheetBB = sheet.get_BoundingBox(null);
            if (sheetBB is null)
                return new { success = false, message = "Could not determine sheet bounds.", moved = 0 };

            double sheetWidth = sheetBB.Max.X - sheetBB.Min.X - 2 * margin;
            double sheetHeight = sheetBB.Max.Y - sheetBB.Min.Y - 2 * margin;
            double originX = sheetBB.Min.X + margin;
            double originY = sheetBB.Min.Y + margin;

            int cols, rows;
            switch (layout)
            {
                case "horizontal":
                    cols = viewports.Count;
                    rows = 1;
                    break;
                case "vertical":
                    cols = 1;
                    rows = viewports.Count;
                    break;
                default: // grid
                    cols = (int)Math.Ceiling(Math.Sqrt(viewports.Count));
                    rows = (int)Math.Ceiling((double)viewports.Count / cols);
                    break;
            }

            double cellWidth = sheetWidth / cols;
            double cellHeight = sheetHeight / rows;

            using var tx = new Transaction(document, "Arrange viewports");
            tx.Start();

            int movedCount = 0;
            for (int i = 0; i < viewports.Count; i++)
            {
                int col = i % cols;
                int row = rows - 1 - i / cols; // top-to-bottom

                double centerX = originX + col * cellWidth + cellWidth / 2;
                double centerY = originY + row * cellHeight + cellHeight / 2;

                try
                {
                    viewports[i].SetBoxCenter(new XYZ(centerX, centerY, 0));
                    movedCount++;
                }
                catch
                {
                    // viewport may be pinned or have constraints
                }
            }

            tx.Commit();
            return new
            {
                success = true,
                message = $"Arranged {movedCount}/{viewports.Count} viewports in {layout} layout ({cols}×{rows}).",
                moved = movedCount
            };
        });

        var r = (dynamic)result!;
        return (bool)r.success
            ? SkillResult.Ok((string)r.message, result)
            : SkillResult.Fail((string)r.message);
    }
}
