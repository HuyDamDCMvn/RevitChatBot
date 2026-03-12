using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("copy_view_to_sheet",
    "Place a view on a sheet as a viewport. Automatically duplicates the view if it is already " +
    "placed on another sheet (except legends which can appear on multiple sheets).")]
[SkillParameter("view_name", "string", "Name of the view to place (partial match).", isRequired: true)]
[SkillParameter("sheet_number", "string", "Target sheet number.", isRequired: true)]
[SkillParameter("position", "string",
    "Position on sheet: 'center', 'top_left', 'top_right', 'bottom_left', 'bottom_right'. Default 'center'.",
    isRequired: false,
    allowedValues: new[] { "center", "top_left", "top_right", "bottom_left", "bottom_right" })]
public class CopyViewToSheetSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context, Dictionary<string, object?> parameters, CancellationToken ct = default)
    {
        if (context.RevitApiInvoker is null) return SkillResult.Fail("Revit API not available.");

        var viewName = parameters.GetValueOrDefault("view_name")?.ToString();
        var sheetNumber = parameters.GetValueOrDefault("sheet_number")?.ToString();
        var position = parameters.GetValueOrDefault("position")?.ToString() ?? "center";

        if (string.IsNullOrWhiteSpace(viewName)) return SkillResult.Fail("'view_name' is required.");
        if (string.IsNullOrWhiteSpace(sheetNumber)) return SkillResult.Fail("'sheet_number' is required.");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var sheet = new FilteredElementCollector(document)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .FirstOrDefault(s => s.SheetNumber.Equals(sheetNumber, StringComparison.OrdinalIgnoreCase));
            if (sheet is null)
                return new { error = $"Sheet '{sheetNumber}' not found." };

            var view = new FilteredElementCollector(document)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && v is not ViewSchedule)
                .FirstOrDefault(v => v.Name.Contains(viewName, StringComparison.OrdinalIgnoreCase));
            if (view is null)
                return new { error = $"View '{viewName}' not found." };

            using var tx = new Transaction(document, "Place View on Sheet");
            tx.Start();

            var viewIdToPlace = view.Id;
            bool duplicated = false;

            if (!Viewport.CanAddViewToSheet(document, sheet.Id, view.Id))
            {
                if (view is ViewPlan || view is ViewSection || view is View3D)
                {
                    viewIdToPlace = view.Duplicate(ViewDuplicateOption.WithDetailing);
                    duplicated = true;
                }
                else
                    return new { error = $"Cannot place view '{view.Name}' on sheet — it may already be placed and cannot be duplicated." };
            }

            var uvOutline = sheet.Outline;
            var loc = CalculatePosition(uvOutline, position);
            var viewport = Viewport.Create(document, sheet.Id, viewIdToPlace, loc);
            tx.Commit();

            return new
            {
                error = (string?)null,
                message = $"Placed '{view.Name}' on sheet {sheetNumber} at {position}." + (duplicated ? " (view was duplicated)" : ""),
                viewportId = viewport.Id.Value,
                duplicated
            };
        });

        var data = result as dynamic;
        if (data?.error is string err && !string.IsNullOrEmpty(err))
            return SkillResult.Fail(err);
        return SkillResult.Ok(data?.message?.ToString() ?? "Done.", result);
    }

    private static XYZ CalculatePosition(BoundingBoxUV uvOutline, string position)
    {
        double cx = (uvOutline.Min.U + uvOutline.Max.U) / 2;
        double cy = (uvOutline.Min.V + uvOutline.Max.V) / 2;
        double qx = (uvOutline.Max.U - uvOutline.Min.U) / 4;
        double qy = (uvOutline.Max.V - uvOutline.Min.V) / 4;

        return position switch
        {
            "top_left" => new XYZ(cx - qx, cy + qy, 0),
            "top_right" => new XYZ(cx + qx, cy + qy, 0),
            "bottom_left" => new XYZ(cx - qx, cy - qy, 0),
            "bottom_right" => new XYZ(cx + qx, cy - qy, 0),
            _ => new XYZ(cx, cy, 0)
        };
    }
}
