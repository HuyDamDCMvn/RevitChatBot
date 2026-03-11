using Autodesk.Revit.DB;

namespace RevitChatBot.RevitServices;

public class RevitElementService : IRevitElementService
{
    public FluentCollector Collect(Document doc) => new(doc);

    public List<Element> GetElementsByCategory(Document doc, BuiltInCategory category)
    {
        return new FluentCollector(doc)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .ToList();
    }

    public List<Element> GetElementsByType(Document doc, Type elementType)
    {
        return new FluentCollector(doc)
            .OfClass(elementType)
            .WhereElementIsNotElementType()
            .ToList();
    }

    public Element? GetElementById(Document doc, ElementId id)
    {
        return doc.GetElement(id);
    }

    public Dictionary<string, string> GetElementParameters(Element element)
    {
        var result = new Dictionary<string, string>();
        foreach (Parameter param in element.Parameters)
        {
            if (!param.HasValue) continue;
            var name = param.Definition.Name;
            var value = param.StorageType switch
            {
                StorageType.Double => param.AsValueString() ?? param.AsDouble().ToString("F2"),
                StorageType.Integer => param.AsValueString() ?? param.AsInteger().ToString(),
                StorageType.String => param.AsString() ?? "",
                StorageType.ElementId => param.AsValueString() ?? param.AsElementId().ToString(),
                _ => param.AsValueString() ?? ""
            };
            result[name] = value;
        }
        return result;
    }

    public bool SetElementParameter(
        Document doc, ElementId elementId, string paramName, string value)
    {
        var element = doc.GetElement(elementId);
        if (element is null) return false;

        var param = element.LookupParameter(paramName);
        if (param is null || param.IsReadOnly) return false;

        using var tx = new Transaction(doc, $"Set {paramName}");
        tx.Start();

        var success = param.StorageType switch
        {
            StorageType.String => SetStringParam(param, value),
            StorageType.Double => double.TryParse(value, out var d) && SetDoubleParam(param, d),
            StorageType.Integer => int.TryParse(value, out var i) && SetIntParam(param, i),
            _ => false
        };

        if (success)
            tx.Commit();
        else
            tx.RollBack();

        return success;
    }

    public bool DeleteElement(Document doc, ElementId elementId)
    {
        using var tx = new Transaction(doc, "Delete element");
        tx.Start();
        var deleted = doc.Delete(elementId);
        tx.Commit();
        return deleted.Count > 0;
    }

    private static bool SetStringParam(Parameter p, string v) { p.Set(v); return true; }
    private static bool SetDoubleParam(Parameter p, double v) { p.Set(v); return true; }
    private static bool SetIntParam(Parameter p, int v) { p.Set(v); return true; }
}
