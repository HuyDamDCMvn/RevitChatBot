using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("create_views_from_rooms",
    "Create enlarged plan views from rooms with automatic crop regions. " +
    "Use action='preview' to see which rooms will get views before creating.")]
[SkillParameter("action", "string", "preview or apply.", isRequired: true,
    allowedValues: new[] { "preview", "apply" })]
[SkillParameter("level", "string", "Level name to find rooms on.", isRequired: false)]
[SkillParameter("room_name_filter", "string", "Filter rooms by name (partial match).", isRequired: false)]
[SkillParameter("view_scale", "integer", "View scale denominator (e.g. 50 for 1:50). Default 50.", isRequired: false)]
[SkillParameter("offset_mm", "integer", "Crop offset around room boundary in mm. Default 500.", isRequired: false)]
public class CreateViewsFromRoomsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context, Dictionary<string, object?> parameters, CancellationToken ct = default)
    {
        if (context.RevitApiInvoker is null) return SkillResult.Fail("Revit API not available.");

        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "preview";
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var roomFilter = parameters.GetValueOrDefault("room_name_filter")?.ToString();
        var scale = 50;
        if (parameters.TryGetValue("view_scale", out var vs) && vs is not null) int.TryParse(vs.ToString(), out scale);
        var offsetMm = 500.0;
        if (parameters.TryGetValue("offset_mm", out var om) && om is not null) double.TryParse(om.ToString(), out offsetMm);
        var offsetFeet = offsetMm / 304.8;

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var collector = new FluentCollector(document)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType();

            if (!string.IsNullOrWhiteSpace(levelFilter)) collector.OnLevel(levelFilter);

            var rooms = collector.ToList()
                .Where(r => r.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() > 0)
                .Where(r => string.IsNullOrWhiteSpace(roomFilter) ||
                    (r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()?.Contains(roomFilter, StringComparison.OrdinalIgnoreCase) == true))
                .ToList();

            if (rooms.Count == 0)
                return new { error = "No rooms found matching the filter.", planned = Array.Empty<object>() };

            var planned = rooms.Select(r => new
            {
                roomId = r.Id.Value,
                roomName = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "Room",
                roomNumber = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
                level = r.LevelId != ElementId.InvalidElementId ? document.GetElement(r.LevelId)?.Name ?? "N/A" : "N/A"
            }).ToList();

            if (action == "preview")
                return new { error = (string?)null, message = $"Preview: {planned.Count} views will be created.", planned, created = 0 };

            var viewFamilyType = new FilteredElementCollector(document)
                .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);

            if (viewFamilyType is null)
                return new { error = "No FloorPlan ViewFamilyType found.", planned, created = 0 };

            var existingNames = new FilteredElementCollector(document)
                .OfClass(typeof(View)).Cast<View>().Select(v => v.Name).ToHashSet();

            using var tx = new Transaction(document, "Create Views from Rooms");
            tx.Start();
            int created = 0;
            var errors = new List<string>();

            foreach (var r in rooms)
            {
                try
                {
                    var roomName = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "Room";
                    var levelElem = document.GetElement(r.LevelId) as Level;
                    if (levelElem is null) continue;

                    var viewName = $"{roomName} - {levelElem.Name}";
                    int suffix = 2;
                    while (existingNames.Contains(viewName))
                        viewName = $"{roomName} - {levelElem.Name} ({suffix++})";

                    var view = ViewPlan.Create(document, viewFamilyType.Id, levelElem.Id);
                    view.Name = viewName;
                    view.Scale = scale;
                    existingNames.Add(viewName);

                    var bb = r.get_BoundingBox(null);
                    if (bb is not null)
                    {
                        view.CropBoxActive = true;
                        view.CropBox = new BoundingBoxXYZ
                        {
                            Min = new XYZ(bb.Min.X - offsetFeet, bb.Min.Y - offsetFeet, bb.Min.Z),
                            Max = new XYZ(bb.Max.X + offsetFeet, bb.Max.Y + offsetFeet, bb.Max.Z)
                        };
                    }

                    created++;
                }
                catch (Exception ex) { errors.Add($"{r.Id.Value}: {ex.Message}"); }
            }

            tx.Commit();
            return new { error = (string?)null,
                message = $"Created {created}/{rooms.Count} views." + (errors.Count > 0 ? $" Errors: {errors.Count}" : ""),
                planned, created, errors };
        });

        var data = result as dynamic;
        if (data?.error is string err && !string.IsNullOrEmpty(err)) return SkillResult.Fail(err);
        return SkillResult.Ok(data?.message?.ToString() ?? "Done.", result);
    }
}
