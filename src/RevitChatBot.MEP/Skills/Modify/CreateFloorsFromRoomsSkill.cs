using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("create_floors_from_rooms",
    "Create floor slabs from room boundaries. ALWAYS use action='preview' first to verify " +
    "room boundaries are valid before creating floors.")]
[SkillParameter("action", "string", "preview or apply.", isRequired: true,
    allowedValues: new[] { "preview", "apply" })]
[SkillParameter("level", "string", "Level name to find rooms on.", isRequired: false)]
[SkillParameter("room_name_filter", "string", "Filter rooms by name.", isRequired: false)]
[SkillParameter("floor_type", "string", "Floor type name (partial match). Uses first available if omitted.", isRequired: false)]
public class CreateFloorsFromRoomsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context, Dictionary<string, object?> parameters, CancellationToken ct = default)
    {
        if (context.RevitApiInvoker is null) return SkillResult.Fail("Revit API not available.");

        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "preview";
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var roomFilter = parameters.GetValueOrDefault("room_name_filter")?.ToString();
        var floorTypeFilter = parameters.GetValueOrDefault("floor_type")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var rooms = new FluentCollector(document)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .ToList()
                .Where(r => r.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() > 0)
                .Where(r => string.IsNullOrWhiteSpace(levelFilter) ||
                    (document.GetElement(r.LevelId) as Level)?.Name
                        .Contains(levelFilter, StringComparison.OrdinalIgnoreCase) == true)
                .Where(r => string.IsNullOrWhiteSpace(roomFilter) ||
                    r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()
                        ?.Contains(roomFilter, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            if (rooms.Count == 0) return new { error = "No rooms found matching the filter." };

            var planned = new List<object>();
            var issues = new List<string>();

            foreach (var r in rooms)
            {
                var room = r as Room;
                if (room is null) { issues.Add($"{r.Id.Value}: Not a Room element"); continue; }

                var roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "Room";
                var segments = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
                if (segments is null || segments.Count == 0)
                {
                    issues.Add($"{roomName} ({r.Id.Value}): Unbounded - no boundary segments");
                    continue;
                }

                var outerLoop = segments[0];
                var curveCount = outerLoop.Count;
                planned.Add(new { roomId = r.Id.Value, roomName, curveCount, boundaryLoops = segments.Count });
            }

            if (action == "preview")
                return new { error = (string?)null,
                    message = $"Preview: {planned.Count} floors can be created. {issues.Count} rooms have issues.",
                    planned, issues, created = 0 };

            var floorType = FindFloorType(document, floorTypeFilter);
            if (floorType is null)
                return new { error = "No floor type found in the project." };

            using var tx = new Transaction(document, "Create Floors from Rooms");
            tx.Start();
            int created = 0;
            var errors = new List<string>();

            foreach (var r in rooms)
            {
                try
                {
                    var room = r as Room;
                    if (room is null) continue;

                    var segments = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
                    if (segments is null || segments.Count == 0) continue;

                    var curveLoop = new CurveLoop();
                    foreach (var seg in segments[0])
                        curveLoop.Append(seg.GetCurve());

                    var levelId = room.LevelId;
                    Floor.Create(document, new List<CurveLoop> { curveLoop }, floorType.Id, levelId);
                    created++;
                }
                catch (Exception ex)
                {
                    var roomName = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? r.Id.Value.ToString();
                    errors.Add($"{roomName}: {ex.Message}");
                }
            }

            if (errors.Count > rooms.Count / 2)
            {
                tx.RollBack();
                return new { error = $"Too many failures ({errors.Count}/{rooms.Count}). Transaction rolled back.",
                    planned, issues, created = 0, errors };
            }

            tx.Commit();
            return new { error = (string?)null,
                message = $"Created {created}/{rooms.Count} floors." + (errors.Count > 0 ? $" Errors: {errors.Count}" : ""),
                planned, issues, created, errors };
        });

        var data = result as dynamic;
        if (data?.error is string err && !string.IsNullOrEmpty(err)) return SkillResult.Fail(err);
        return SkillResult.Ok(data?.message?.ToString() ?? "Done.", result);
    }

    private static FloorType? FindFloorType(Document doc, string? filter)
    {
        var types = new FilteredElementCollector(doc)
            .OfClass(typeof(FloorType)).Cast<FloorType>().ToList();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var match = types.FirstOrDefault(t => t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return types.FirstOrDefault();
    }
}
