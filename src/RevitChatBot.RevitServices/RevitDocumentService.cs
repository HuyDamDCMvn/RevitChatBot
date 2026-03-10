using Autodesk.Revit.DB;

namespace RevitChatBot.RevitServices;

public class RevitDocumentService : IRevitDocumentService
{
    public string GetProjectName(Document doc) =>
        doc.ProjectInformation?.Name ?? doc.Title;

    public string GetProjectNumber(Document doc) =>
        doc.ProjectInformation?.Number ?? "N/A";

    public List<string> GetLevelNames(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .Select(l => $"{l.Name} (Elev: {l.Elevation:F2})")
            .ToList();
    }

    public List<string> GetViewNames(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate)
            .Select(v => $"{v.ViewType}: {v.Name}")
            .ToList();
    }

    public Dictionary<string, string> GetProjectInfo(Document doc)
    {
        var info = doc.ProjectInformation;
        if (info is null) return new Dictionary<string, string> { ["status"] = "No project info" };

        var result = new Dictionary<string, string>
        {
            ["Name"] = info.Name ?? "N/A",
            ["Number"] = info.Number ?? "N/A",
            ["Client"] = info.ClientName ?? "N/A",
            ["Address"] = info.Address ?? "N/A",
            ["BuildingName"] = info.BuildingName ?? "N/A",
            ["Author"] = info.Author ?? "N/A",
            ["Status"] = info.Status ?? "N/A",
            ["IssueDate"] = info.IssueDate ?? "N/A"
        };

        return result;
    }
}
