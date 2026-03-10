using Autodesk.Revit.DB;
using RevitChatBot.Core.Context;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Context;

public class SelectedElementsProvider : IContextProvider
{
    public string Name => "selected_elements";
    public int Priority => 30;

    public Func<ICollection<ElementId>>? GetSelection { get; set; }

    public Task<ContextData> GatherAsync(object? revitDocument = null)
    {
        var data = new ContextData();

        if (revitDocument is not Document doc || GetSelection is null)
        {
            data.Add("selected_elements", "No selection available.");
            return Task.FromResult(data);
        }

        var selectedIds = GetSelection();
        if (selectedIds.Count == 0)
        {
            data.Add("selected_elements", "No elements selected.");
            return Task.FromResult(data);
        }

        var service = new RevitElementService();
        var summaries = selectedIds
            .Take(10)
            .Select(id =>
            {
                var el = doc.GetElement(id);
                if (el is null) return $"  ID {id.Value}: not found";

                var typeName = doc.GetElement(el.GetTypeId())?.Name ?? "N/A";
                return $"  ID {id.Value}: {el.Name} ({el.Category?.Name}) Type={typeName}";
            });

        var text = $"Selected: {selectedIds.Count} element(s)\n" +
                   string.Join("\n", summaries);

        if (selectedIds.Count > 10)
            text += $"\n  ... and {selectedIds.Count - 10} more";

        data.Add("selected_elements", text);
        return Task.FromResult(data);
    }
}
