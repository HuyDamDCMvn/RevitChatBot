using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("set_crop_region",
    "Set view crop region from a room boundary, selected elements' bounding box, or manual offset. " +
    "Works with plan and section views. For 3D views, sets the section box instead.")]
[SkillParameter("source", "string",
    "Source: 'room' (from room boundary), 'selected' (from selected elements' bounding box).",
    isRequired: true, allowedValues: new[] { "room", "selected" })]
[SkillParameter("room_name", "string", "Room name for source='room'.", isRequired: false)]
[SkillParameter("offset_mm", "integer", "Offset around boundary in mm. Default 300.", isRequired: false)]
public class SetCropRegionSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context, Dictionary<string, object?> parameters, CancellationToken ct = default)
    {
        if (context.RevitApiInvoker is null) return SkillResult.Fail("Revit API not available.");

        var source = parameters.GetValueOrDefault("source")?.ToString() ?? "room";
        var roomName = parameters.GetValueOrDefault("room_name")?.ToString();
        var offsetMm = 300.0;
        if (parameters.TryGetValue("offset_mm", out var om) && om is not null)
            double.TryParse(om.ToString(), out offsetMm);
        var offsetFeet = offsetMm / 304.8;

        var selectionIds = context.GetCurrentSelectionIds();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var activeView = document.ActiveView;
            if (activeView is null) return new { error = "No active view." };

            BoundingBoxXYZ? bbox = null;

            if (source == "room")
            {
                if (string.IsNullOrWhiteSpace(roomName))
                    return new { error = "Parameter 'room_name' is required when source='room'." };

                var room = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .FirstOrDefault(r => r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()
                        ?.Contains(roomName, StringComparison.OrdinalIgnoreCase) == true);

                if (room is null) return new { error = $"Room '{roomName}' not found." };
                bbox = room.get_BoundingBox(null);
            }
            else
            {
                if (selectionIds is null || selectionIds.Count == 0)
                    return new { error = "No elements selected." };

                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

                foreach (var id in selectionIds)
                {
                    var elem = document.GetElement(new ElementId(id));
                    var ebb = elem?.get_BoundingBox(null);
                    if (ebb is null) continue;
                    minX = Math.Min(minX, ebb.Min.X); minY = Math.Min(minY, ebb.Min.Y); minZ = Math.Min(minZ, ebb.Min.Z);
                    maxX = Math.Max(maxX, ebb.Max.X); maxY = Math.Max(maxY, ebb.Max.Y); maxZ = Math.Max(maxZ, ebb.Max.Z);
                }

                if (minX < double.MaxValue)
                    bbox = new BoundingBoxXYZ { Min = new XYZ(minX, minY, minZ), Max = new XYZ(maxX, maxY, maxZ) };
            }

            if (bbox is null) return new { error = "Could not determine bounding box." };

            using var tx = new Transaction(document, "Set Crop Region");
            tx.Start();

            if (activeView is View3D view3D)
            {
                view3D.IsSectionBoxActive = true;
                view3D.SetSectionBox(new BoundingBoxXYZ
                {
                    Min = new XYZ(bbox.Min.X - offsetFeet, bbox.Min.Y - offsetFeet, bbox.Min.Z - offsetFeet),
                    Max = new XYZ(bbox.Max.X + offsetFeet, bbox.Max.Y + offsetFeet, bbox.Max.Z + offsetFeet)
                });
            }
            else
            {
                activeView.CropBoxActive = true;
                activeView.CropBox = new BoundingBoxXYZ
                {
                    Min = new XYZ(bbox.Min.X - offsetFeet, bbox.Min.Y - offsetFeet, bbox.Min.Z),
                    Max = new XYZ(bbox.Max.X + offsetFeet, bbox.Max.Y + offsetFeet, bbox.Max.Z)
                };
                activeView.CropBoxVisible = true;
            }

            tx.Commit();
            return new
            {
                error = (string?)null,
                message = $"Crop region set on '{activeView.Name}' from {source}.",
                viewName = activeView.Name,
                viewType = activeView.ViewType.ToString(),
                is3D = activeView is View3D
            };
        });

        var data = result as dynamic;
        if (data?.error is string err && !string.IsNullOrEmpty(err)) return SkillResult.Fail(err);
        return SkillResult.Ok(data?.message?.ToString() ?? "Done.", result);
    }
}
