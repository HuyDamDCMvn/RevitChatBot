using Autodesk.Revit.DB;
using RevitChatBot.Core.Context;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Context;

public class ProjectInfoProvider : IContextProvider
{
    public string Name => "project_info";
    public int Priority => 10;

    public Task<ContextData> GatherAsync(object? revitDocument = null)
    {
        var data = new ContextData();

        if (revitDocument is not Document doc)
        {
            data.Add("project_info", "No active document.");
            return Task.FromResult(data);
        }

        var service = new RevitDocumentService();
        var info = service.GetProjectInfo(doc);
        var levels = service.GetLevelNames(doc);

        var text = string.Join("\n", info.Select(kv => $"  {kv.Key}: {kv.Value}"));
        text += $"\n  Levels ({levels.Count}): {string.Join(", ", levels.Take(10))}";

        data.Add("project_info", text);
        return Task.FromResult(data);
    }
}
