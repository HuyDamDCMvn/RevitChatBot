using Autodesk.Revit.DB;
using RevitChatBot.Core.Context;

namespace RevitChatBot.MEP.Context;

public class ActiveViewProvider : IContextProvider
{
    public string Name => "active_view";
    public int Priority => 20;

    public Task<ContextData> GatherAsync(object? revitDocument = null)
    {
        var data = new ContextData();

        if (revitDocument is not Document doc)
        {
            data.Add("active_view", "No active document.");
            return Task.FromResult(data);
        }

        var view = doc.ActiveView;
        if (view is null)
        {
            data.Add("active_view", "No active view.");
            return Task.FromResult(data);
        }

        var info = $"Name: {view.Name}\n" +
                   $"  Type: {view.ViewType}\n" +
                   $"  Level: {view.GenLevel?.Name ?? "N/A"}\n" +
                   $"  Scale: 1:{view.Scale}";

        data.Add("active_view", info);
        return Task.FromResult(data);
    }
}
