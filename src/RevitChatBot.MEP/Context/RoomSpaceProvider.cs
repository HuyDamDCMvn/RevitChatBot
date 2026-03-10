using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using RevitChatBot.Core.Context;

namespace RevitChatBot.MEP.Context;

/// <summary>
/// Provides context about Rooms and MEP Spaces in the model,
/// including area, volume, and HVAC zone information.
/// </summary>
public class RoomSpaceProvider : IContextProvider
{
    public string Name => "rooms_spaces";
    public int Priority => 35;

    public Task<ContextData> GatherAsync(object? revitDocument = null)
    {
        var data = new ContextData();

        if (revitDocument is not Document doc)
        {
            data.Add("rooms_spaces", "No active document.");
            return Task.FromResult(data);
        }

        var spaces = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_MEPSpaces)
            .WhereElementIsNotElementType()
            .Cast<Space>()
            .ToList();

        var rooms = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .ToList();

        var spacesByLevel = spaces
            .GroupBy(s => s.Level?.Name ?? "Unknown")
            .Select(g => new
            {
                Level = g.Key,
                Count = g.Count(),
                TotalAreaFt2 = g.Sum(s => s.Area),
                TotalVolumeFt3 = g.Sum(s => s.Volume)
            })
            .OrderBy(g => g.Level)
            .ToList();

        var lines = new List<string>
        {
            $"Rooms: {rooms.Count}",
            $"MEP Spaces: {spaces.Count}"
        };

        if (spacesByLevel.Count > 0)
        {
            lines.Add("\nSpaces by Level:");
            foreach (var lvl in spacesByLevel.Take(10))
            {
                lines.Add($"  - {lvl.Level}: {lvl.Count} spaces, " +
                          $"{Math.Round(lvl.TotalAreaFt2 * 0.092903, 1)} m²");
            }
            if (spacesByLevel.Count > 10)
                lines.Add($"  ... and {spacesByLevel.Count - 10} more levels");
        }

        var unplacedSpaces = spaces.Count(s => s.Area == 0);
        if (unplacedSpaces > 0)
            lines.Add($"\nWarning: {unplacedSpaces} unplaced/unbounded spaces detected.");

        data.Add("rooms_spaces", string.Join("\n", lines));
        return Task.FromResult(data);
    }
}
