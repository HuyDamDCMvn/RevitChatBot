using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Query;

[Skill("worksharing_info",
    "Get worksharing/collaboration info for elements: creator, current owner, " +
    "last modifier, workset, and sync status with the central model. " +
    "Works only in workshared Revit models. Supports querying by element IDs, " +
    "by owner name, by workset, or finding elements with editing conflicts.")]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs to inspect. Use this for detailed info on specific elements.",
    isRequired: false)]
[SkillParameter("query_mode", "string",
    "'by_owner' to list elements owned by a user, " +
    "'by_workset' to list elements in a workset, " +
    "'not_synced' to find elements checked out locally.",
    isRequired: false,
    allowedValues: new[] { "by_owner", "by_workset", "not_synced" })]
[SkillParameter("user_name", "string",
    "User name for 'by_owner' query (partial match supported).",
    isRequired: false)]
[SkillParameter("workset_name", "string",
    "Workset name for 'by_workset' query.",
    isRequired: false)]
[SkillParameter("category", "string",
    "Optional category filter for query modes: 'ducts', 'pipes', 'equipment', etc.",
    isRequired: false)]
[SkillParameter("max_results", "integer",
    "Maximum results for query modes. Default: 50.",
    isRequired: false)]
public class WorksharingInfoSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ducts"] = BuiltInCategory.OST_DuctCurves,
        ["pipes"] = BuiltInCategory.OST_PipeCurves,
        ["equipment"] = BuiltInCategory.OST_MechanicalEquipment,
        ["fittings"] = BuiltInCategory.OST_DuctFitting,
        ["air_terminals"] = BuiltInCategory.OST_DuctTerminal,
        ["electrical"] = BuiltInCategory.OST_ElectricalEquipment,
        ["plumbing"] = BuiltInCategory.OST_PlumbingFixtures,
        ["cable_trays"] = BuiltInCategory.OST_CableTray,
        ["conduits"] = BuiltInCategory.OST_Conduit,
        ["sprinklers"] = BuiltInCategory.OST_Sprinklers,
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var idsStr = parameters.GetValueOrDefault("element_ids")?.ToString();
        var queryMode = parameters.GetValueOrDefault("query_mode")?.ToString();
        var userName = parameters.GetValueOrDefault("user_name")?.ToString();
        var worksetName = parameters.GetValueOrDefault("workset_name")?.ToString();
        var categoryStr = parameters.GetValueOrDefault("category")?.ToString();
        var maxResults = ParseInt(parameters.GetValueOrDefault("max_results"), 50);

        if (string.IsNullOrWhiteSpace(idsStr) && string.IsNullOrWhiteSpace(queryMode))
            return SkillResult.Fail("Provide 'element_ids' for specific elements or 'query_mode' for a broader query.");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            if (!document.IsWorkshared)
                return new WorksharingResult { Error = "This is not a workshared model. Worksharing info is unavailable." };

            if (!string.IsNullOrWhiteSpace(idsStr))
                return GetElementDetails(document, idsStr);

            return queryMode switch
            {
                "by_owner" => QueryByOwner(document, userName, categoryStr, maxResults),
                "by_workset" => QueryByWorkset(document, worksetName, categoryStr, maxResults),
                "not_synced" => QueryNotSynced(document, categoryStr, maxResults),
                _ => new WorksharingResult { Error = $"Unknown query_mode '{queryMode}'." }
            };
        });

        var res = result as WorksharingResult;
        if (res is null || !res.Success)
            return SkillResult.Fail(res?.Error ?? "Worksharing info failed.");

        return SkillResult.Ok(res.Message ?? "Worksharing info retrieved.", result);
    }

    private static WorksharingResult GetElementDetails(Document doc, string idsStr)
    {
        var ids = ParseElementIds(idsStr);
        var details = new List<ElementSharingInfo>();
        var notFound = new List<string>();

        foreach (var id in ids)
        {
            var elemId = new ElementId(id);
            var elem = doc.GetElement(elemId);
            if (elem is null) { notFound.Add(id.ToString()); continue; }

            var info = BuildSharingInfo(doc, elem);
            details.Add(info);
        }

        return new WorksharingResult
        {
            Success = true,
            Message = $"Retrieved sharing info for {details.Count} element(s)." +
                      (notFound.Count > 0 ? $" {notFound.Count} not found." : ""),
            Elements = details,
            NotFound = notFound,
            TotalCount = details.Count
        };
    }

    private static WorksharingResult QueryByOwner(Document doc, string? userName, string? categoryStr, int max)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return new WorksharingResult { Error = "Parameter 'user_name' is required for 'by_owner' query." };

        var elements = CollectElements(doc, categoryStr);
        var matched = new List<ElementSharingInfo>();
        var categorySummary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var elem in elements)
        {
            try
            {
                var tooltip = WorksharingUtils.GetWorksharingTooltipInfo(doc, elem.Id);
                if (tooltip.Owner?.Contains(userName, StringComparison.OrdinalIgnoreCase) != true &&
                    tooltip.LastChangedBy?.Contains(userName, StringComparison.OrdinalIgnoreCase) != true)
                    continue;

                var catName = elem.Category?.Name ?? "Unknown";
                categorySummary[catName] = categorySummary.GetValueOrDefault(catName) + 1;

                if (matched.Count < max)
                    matched.Add(BuildSharingInfo(doc, elem));
            }
            catch { /* skip elements that can't be queried */ }
        }

        var totalMatched = categorySummary.Values.Sum();
        return new WorksharingResult
        {
            Success = true,
            Message = $"Found {totalMatched} elements associated with '{userName}'.",
            Elements = matched,
            TotalCount = totalMatched,
            CategorySummary = categorySummary,
            QueryUser = userName
        };
    }

    private static WorksharingResult QueryByWorkset(Document doc, string? worksetName, string? categoryStr, int max)
    {
        if (string.IsNullOrWhiteSpace(worksetName))
            return new WorksharingResult { Error = "Parameter 'workset_name' is required for 'by_workset' query." };

        var table = doc.GetWorksetTable();
        Workset? targetWorkset = null;

        var worksets = new FilteredWorksetCollector(doc)
            .OfKind(WorksetKind.UserWorkset)
            .ToWorksets();

        foreach (var ws in worksets)
        {
            if (ws.Name.Contains(worksetName, StringComparison.OrdinalIgnoreCase))
            {
                targetWorkset = ws;
                break;
            }
        }

        if (targetWorkset is null)
            return new WorksharingResult { Error = $"Workset containing '{worksetName}' not found." };

        var elements = CollectElements(doc, categoryStr);
        var matched = new List<ElementSharingInfo>();

        foreach (var elem in elements)
        {
            if (elem.WorksetId != targetWorkset.Id) continue;

            if (matched.Count < max)
                matched.Add(BuildSharingInfo(doc, elem));
        }

        return new WorksharingResult
        {
            Success = true,
            Message = $"Found {matched.Count} elements in workset '{targetWorkset.Name}'.",
            Elements = matched,
            TotalCount = matched.Count,
            QueryWorkset = targetWorkset.Name
        };
    }

    private static WorksharingResult QueryNotSynced(Document doc, string? categoryStr, int max)
    {
        var elements = CollectElements(doc, categoryStr);
        var matched = new List<ElementSharingInfo>();
        var categorySummary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var elem in elements)
        {
            try
            {
                var status = WorksharingUtils.GetCheckoutStatus(doc, elem.Id);
                if (status != CheckoutStatus.OwnedByCurrentUser) continue;

                var catName = elem.Category?.Name ?? "Unknown";
                categorySummary[catName] = categorySummary.GetValueOrDefault(catName) + 1;

                if (matched.Count < max)
                    matched.Add(BuildSharingInfo(doc, elem));
            }
            catch { }
        }

        var totalMatched = categorySummary.Values.Sum();
        return new WorksharingResult
        {
            Success = true,
            Message = $"Found {totalMatched} elements checked out by you (not yet synced).",
            Elements = matched,
            TotalCount = totalMatched,
            CategorySummary = categorySummary
        };
    }

    private static ElementSharingInfo BuildSharingInfo(Document doc, Element elem)
    {
        var info = new ElementSharingInfo
        {
            Id = elem.Id.Value,
            Name = elem.Name,
            Category = elem.Category?.Name ?? ""
        };

        try
        {
            var tooltip = WorksharingUtils.GetWorksharingTooltipInfo(doc, elem.Id);
            info.Creator = tooltip.Creator ?? "";
            info.Owner = tooltip.Owner ?? "";
            info.LastChangedBy = tooltip.LastChangedBy ?? "";
        }
        catch { }

        try
        {
            var wsTable = doc.GetWorksetTable();
            if (elem.WorksetId != WorksetId.InvalidWorksetId)
            {
                var ws = wsTable.GetWorkset(elem.WorksetId);
                info.WorksetName = ws?.Name ?? "";
            }
        }
        catch { }

        try
        {
            info.CheckoutStatus = WorksharingUtils.GetCheckoutStatus(doc, elem.Id).ToString();
        }
        catch { }

        try
        {
            info.ModelUpdatesStatus = WorksharingUtils.GetModelUpdatesStatus(doc, elem.Id).ToString();
        }
        catch { }

        return info;
    }

    private static List<Element> CollectElements(Document doc, string? categoryStr)
    {
        if (!string.IsNullOrWhiteSpace(categoryStr) && CategoryMap.TryGetValue(categoryStr.Trim(), out var bic))
        {
            return new FluentCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToList();
        }

        return new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .Where(e => e.Category is not null)
            .Take(5000)
            .ToList();
    }

    private static List<long> ParseElementIds(string idsStr)
    {
        return idsStr
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s.Trim(), out var id) ? id : -1)
            .Where(id => id > 0)
            .ToList();
    }

    private static int ParseInt(object? value, int fallback)
    {
        if (value is int i) return i;
        if (value is string s && int.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }

    private class WorksharingResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        public List<ElementSharingInfo> Elements { get; set; } = [];
        public List<string> NotFound { get; set; } = [];
        public int TotalCount { get; set; }
        public Dictionary<string, int> CategorySummary { get; set; } = new();
        public string? QueryUser { get; set; }
        public string? QueryWorkset { get; set; }
    }

    private class ElementSharingInfo
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Creator { get; set; } = "";
        public string Owner { get; set; } = "";
        public string LastChangedBy { get; set; } = "";
        public string WorksetName { get; set; } = "";
        public string CheckoutStatus { get; set; } = "";
        public string ModelUpdatesStatus { get; set; } = "";
    }
}
