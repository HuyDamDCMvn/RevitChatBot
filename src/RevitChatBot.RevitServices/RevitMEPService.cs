using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace RevitChatBot.RevitServices;

public class RevitMEPService : IRevitMEPService
{
    public List<Element> GetMEPSystems(Document doc)
    {
        var mechanical = new FilteredElementCollector(doc)
            .OfClass(typeof(MechanicalSystem))
            .ToList();

        var piping = new FilteredElementCollector(doc)
            .OfClass(typeof(PipingSystem))
            .ToList();

        return mechanical.Concat(piping).ToList();
    }

    public List<Element> GetDucts(Document doc, string? systemName = null)
    {
        var collector = new FilteredElementCollector(doc)
            .OfClass(typeof(Duct))
            .WhereElementIsNotElementType();

        if (systemName is not null)
        {
            return collector
                .Cast<Duct>()
                .Where(d => d.MEPSystem?.Name?.Contains(systemName, StringComparison.OrdinalIgnoreCase) == true)
                .Cast<Element>()
                .ToList();
        }

        return collector.ToList();
    }

    public List<Element> GetPipes(Document doc, string? systemName = null)
    {
        var collector = new FilteredElementCollector(doc)
            .OfClass(typeof(Pipe))
            .WhereElementIsNotElementType();

        if (systemName is not null)
        {
            return collector
                .Cast<Pipe>()
                .Where(p => p.MEPSystem?.Name?.Contains(systemName, StringComparison.OrdinalIgnoreCase) == true)
                .Cast<Element>()
                .ToList();
        }

        return collector.ToList();
    }

    public List<Element> GetMEPEquipment(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
            .WhereElementIsNotElementType()
            .ToList();
    }

    public List<Element> GetFittings(Document doc, BuiltInCategory category)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .ToList();
    }

    public Dictionary<string, object> GetMEPSystemInfo(Element system)
    {
        var info = new Dictionary<string, object>
        {
            ["Name"] = system.Name,
            ["Id"] = system.Id.Value,
            ["Category"] = system.Category?.Name ?? "Unknown"
        };

        if (system is MechanicalSystem mechSys)
        {
            info["SystemType"] = mechSys.SystemType.ToString();
            info["ElementCount"] = mechSys.DuctNetwork?.Size ?? 0;
        }
        else if (system is PipingSystem pipeSys)
        {
            info["SystemType"] = pipeSys.SystemType.ToString();
            info["ElementCount"] = pipeSys.PipingNetwork?.Size ?? 0;
        }

        return info;
    }
}
