using Autodesk.Revit.DB;
using RevitChatBot.Core.Context;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Context;

public class MEPSystemProvider : IContextProvider
{
    public string Name => "mep_systems";
    public int Priority => 40;

    public Task<ContextData> GatherAsync(object? revitDocument = null)
    {
        var data = new ContextData();

        if (revitDocument is not Document doc)
        {
            data.Add("mep_systems", "No active document.");
            return Task.FromResult(data);
        }

        var mepService = new RevitMEPService();
        var systems = mepService.GetMEPSystems(doc);
        var ducts = mepService.GetDucts(doc);
        var pipes = mepService.GetPipes(doc);
        var equipment = mepService.GetMEPEquipment(doc);

        var systemNames = systems
            .Take(20)
            .Select(s =>
            {
                var info = mepService.GetMEPSystemInfo(s);
                var name = info.GetValueOrDefault("Name", "Unknown");
                var type = info.GetValueOrDefault("SystemType", "Unknown");
                var count = info.GetValueOrDefault("ElementCount", 0);
                return $"  - {name} ({type}, {count} elements)";
            });

        var text = $"MEP Overview:\n" +
                   $"  Systems: {systems.Count}\n" +
                   $"  Ducts: {ducts.Count}\n" +
                   $"  Pipes: {pipes.Count}\n" +
                   $"  Equipment: {equipment.Count}\n" +
                   $"\nSystems:\n{string.Join("\n", systemNames)}";

        if (systems.Count > 20)
            text += $"\n  ... and {systems.Count - 20} more systems";

        data.Add("mep_systems", text);
        return Task.FromResult(data);
    }
}
