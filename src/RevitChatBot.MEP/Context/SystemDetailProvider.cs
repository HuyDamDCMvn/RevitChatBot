using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;
using RevitChatBot.Core.Context;

namespace RevitChatBot.MEP.Context;

/// <summary>
/// Provides detailed breakdown of MEP systems by discipline:
/// mechanical system types, piping system types, electrical circuits.
/// </summary>
public class SystemDetailProvider : IContextProvider
{
    public string Name => "system_detail";
    public int Priority => 45;

    public Task<ContextData> GatherAsync(object? revitDocument = null)
    {
        var data = new ContextData();

        if (revitDocument is not Document doc)
        {
            data.Add("system_detail", "No active document.");
            return Task.FromResult(data);
        }

        var lines = new List<string>();

        var mechSystems = new FilteredElementCollector(doc)
            .OfClass(typeof(MechanicalSystem))
            .Cast<MechanicalSystem>()
            .ToList();

        if (mechSystems.Count > 0)
        {
            var byType = mechSystems
                .GroupBy(s => s.SystemType.ToString())
                .Select(g => $"  - {g.Key}: {g.Count()} systems")
                .ToList();

            lines.Add("HVAC Systems:");
            lines.AddRange(byType);
        }

        var pipeSystems = new FilteredElementCollector(doc)
            .OfClass(typeof(PipingSystem))
            .Cast<PipingSystem>()
            .ToList();

        if (pipeSystems.Count > 0)
        {
            var byType = pipeSystems
                .GroupBy(s => s.SystemType.ToString())
                .Select(g => $"  - {g.Key}: {g.Count()} systems")
                .ToList();

            lines.Add("\nPlumbing Systems:");
            lines.AddRange(byType);
        }

        var elecSystems = new FilteredElementCollector(doc)
            .OfClass(typeof(ElectricalSystem))
            .Cast<ElectricalSystem>()
            .ToList();

        if (elecSystems.Count > 0)
        {
            lines.Add($"\nElectrical: {elecSystems.Count} circuits");

            var panelCount = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .GetElementCount();
            lines.Add($"  Panels: {panelCount}");

            var conduitCount = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Conduit)
                .WhereElementIsNotElementType()
                .GetElementCount();
            var cableTrayCount = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_CableTray)
                .WhereElementIsNotElementType()
                .GetElementCount();

            lines.Add($"  Conduits: {conduitCount}");
            lines.Add($"  Cable Trays: {cableTrayCount}");
        }

        if (lines.Count == 0)
            lines.Add("No MEP systems found in the model.");

        data.Add("system_detail", string.Join("\n", lines));
        return Task.FromResult(data);
    }
}
