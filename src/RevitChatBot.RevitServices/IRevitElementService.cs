using Autodesk.Revit.DB;

namespace RevitChatBot.RevitServices;

public interface IRevitElementService
{
    FluentCollector Collect(Document doc);
    List<Element> GetElementsByCategory(Document doc, BuiltInCategory category);
    List<Element> GetElementsByType(Document doc, Type elementType);
    Element? GetElementById(Document doc, ElementId id);
    Dictionary<string, string> GetElementParameters(Element element);
    bool SetElementParameter(Document doc, ElementId elementId, string paramName, string value);
    bool DeleteElement(Document doc, ElementId elementId);
}

public interface IRevitDocumentService
{
    string GetProjectName(Document doc);
    string GetProjectNumber(Document doc);
    List<string> GetLevelNames(Document doc);
    List<string> GetViewNames(Document doc);
    Dictionary<string, string> GetProjectInfo(Document doc);
}

public interface IRevitMEPService
{
    FluentCollector Collect(Document doc);
    List<Element> GetMEPSystems(Document doc);
    List<Element> GetDucts(Document doc, string? systemName = null);
    List<Element> GetPipes(Document doc, string? systemName = null);
    List<Element> GetMEPEquipment(Document doc);
    List<Element> GetFittings(Document doc, BuiltInCategory category);
    Dictionary<string, object> GetMEPSystemInfo(Element system);
}
