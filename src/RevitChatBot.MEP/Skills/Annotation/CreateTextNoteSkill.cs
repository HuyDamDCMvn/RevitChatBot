using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Annotation;

[Skill("create_text_note",
    "Create a text note in the active view at a specified location or near an element. " +
    "Use for adding annotations, warnings, instructions, or labels on drawings.")]
[SkillParameter("text", "string",
    "Text content of the note.",
    isRequired: true)]
[SkillParameter("near_element_id", "string",
    "Place the note near this element (by its bounding box center). Optional.",
    isRequired: false)]
[SkillParameter("x_mm", "number",
    "X coordinate in mm (if not using near_element_id). Default: 0.",
    isRequired: false)]
[SkillParameter("y_mm", "number",
    "Y coordinate in mm (if not using near_element_id). Default: 0.",
    isRequired: false)]
[SkillParameter("offset_x_mm", "number",
    "Additional X offset from element position in mm. Default: 500.",
    isRequired: false)]
[SkillParameter("offset_y_mm", "number",
    "Additional Y offset from element position in mm. Default: 300.",
    isRequired: false)]
public class CreateTextNoteSkill : ISkill
{
    private const double MmToFeet = 1.0 / 304.8;

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var text = parameters.GetValueOrDefault("text")?.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return SkillResult.Fail("'text' is required.");

        var nearElemId = parameters.GetValueOrDefault("near_element_id")?.ToString();
        var xMm = Convert.ToDouble(parameters.GetValueOrDefault("x_mm") ?? 0);
        var yMm = Convert.ToDouble(parameters.GetValueOrDefault("y_mm") ?? 0);
        var offsetX = Convert.ToDouble(parameters.GetValueOrDefault("offset_x_mm") ?? 500) * MmToFeet;
        var offsetY = Convert.ToDouble(parameters.GetValueOrDefault("offset_y_mm") ?? 300) * MmToFeet;

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var view = document.ActiveView;
            if (view is null || view is ViewSheet)
                return new { status = "error", message = "Active view is not suitable for text notes.", noteId = -1L };

            XYZ position;
            if (!string.IsNullOrWhiteSpace(nearElemId) && long.TryParse(nearElemId, out var elemIdVal))
            {
                var elem = document.GetElement(new ElementId(elemIdVal));
                if (elem is null)
                    return new { status = "error", message = $"Element {nearElemId} not found.", noteId = -1L };

                var bb = elem.get_BoundingBox(view) ?? elem.get_BoundingBox(null);
                if (bb is null)
                    return new { status = "error", message = "Cannot determine element position.", noteId = -1L };

                var center = (bb.Min + bb.Max) / 2.0;
                position = new XYZ(center.X + offsetX, center.Y + offsetY, 0);
            }
            else
            {
                position = new XYZ(xMm * MmToFeet, yMm * MmToFeet, 0);
            }

            var defaultTypeId = document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            if (defaultTypeId == ElementId.InvalidElementId)
            {
                var allTypes = new FilteredElementCollector(document)
                    .OfClass(typeof(TextNoteType))
                    .FirstElementId();
                defaultTypeId = allTypes;
            }

            if (defaultTypeId == ElementId.InvalidElementId)
                return new { status = "error", message = "No TextNoteType found in the project.", noteId = -1L };

            using var tx = new Transaction(document, "Create text note");
            tx.Start();
            try
            {
                var options = new TextNoteOptions(defaultTypeId)
                {
                    HorizontalAlignment = HorizontalTextAlignment.Left
                };
                var note = TextNote.Create(document, view.Id, position, text!, options);
                tx.Commit();

                return new
                {
                    status = "ok",
                    message = $"Text note created in '{view.Name}': \"{text}\".",
                    noteId = note.Id.Value
                };
            }
            catch (Exception ex)
            {
                if (tx.HasStarted()) tx.RollBack();
                return new { status = "error", message = ex.Message, noteId = -1L };
            }
        });

        dynamic res = result!;
        return res.status == "ok"
            ? SkillResult.Ok(res.message, result)
            : SkillResult.Fail(res.message);
    }
}
