using Autodesk.Revit.DB;
using RevitChatBot.Core.Context;

namespace RevitChatBot.MEP.Context;

/// <summary>
/// Provides a summary of MEP element counts per level for spatial awareness.
/// </summary>
public class LevelSummaryProvider : IContextProvider
{
    public string Name => "level_summary";
    public int Priority => 30;

    public Task<ContextData> GatherAsync(object? revitDocument = null)
    {
        var data = new ContextData();

        if (revitDocument is not Document doc)
        {
            data.Add("level_summary", "No active document.");
            return Task.FromResult(data);
        }

        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .ToList();

        if (levels.Count == 0)
        {
            data.Add("level_summary", "No levels found.");
            return Task.FromResult(data);
        }

        var mepCategories = new[]
        {
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_PlumbingFixtures
        };

        var lines = new List<string> { "MEP Elements by Level:" };

        foreach (var level in levels.Take(15))
        {
            int total = 0;
            foreach (var cat in mepCategories)
            {
                total += new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .Where(e => e.LevelId == level.Id)
                    .Count();
            }

            var elev = Math.Round(level.Elevation * 0.3048, 2);
            lines.Add($"  - {level.Name} (elev: {elev}m): {total} MEP elements");
        }

        if (levels.Count > 15)
            lines.Add($"  ... and {levels.Count - 15} more levels");

        data.Add("level_summary", string.Join("\n", lines));
        return Task.FromResult(data);
    }
}
