using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("bulk_rename",
    "Bulk rename views, sheets, rooms, levels, grids, view templates, schedules, spaces, and areas. " +
    "Operations: add_prefix, add_suffix, find_replace, remove_prefix, remove_suffix, regex_replace. " +
    "Run with action='preview' first to review changes before applying.")]
[SkillParameter("action", "string",
    "'preview' to show proposed changes, 'apply' to rename.",
    isRequired: true,
    allowedValues: new[] { "preview", "apply" })]
[SkillParameter("target_type", "string",
    "What to rename: 'views', 'sheets', 'rooms', 'spaces', 'levels', 'grids', " +
    "'view_templates', 'schedules', 'areas'.",
    isRequired: true,
    allowedValues: new[] { "views", "sheets", "rooms", "spaces", "levels", "grids",
                           "view_templates", "schedules", "areas" })]
[SkillParameter("operation", "string",
    "Rename operation type.",
    isRequired: true,
    allowedValues: new[] { "add_prefix", "add_suffix", "find_replace",
                           "remove_prefix", "remove_suffix", "regex_replace" })]
[SkillParameter("value", "string",
    "The prefix/suffix text, or the 'find' text for find_replace/regex_replace.",
    isRequired: true)]
[SkillParameter("replace_with", "string",
    "Replacement text for find_replace/regex_replace. Default: '' (removes found text).",
    isRequired: false)]
[SkillParameter("name_filter", "string",
    "Only rename items whose current name contains this text.",
    isRequired: false)]
[SkillParameter("view_type_filter", "string",
    "For views only: filter by view type: 'floor_plans', 'ceiling_plans', 'sections', " +
    "'elevations', '3d', 'engineering_plans', 'area_plans'.",
    isRequired: false)]
public class BulkRenameSkill : ISkill
{
    private static readonly Dictionary<string, ViewType[]> ViewTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["floor_plans"] = [ViewType.FloorPlan],
        ["ceiling_plans"] = [ViewType.CeilingPlan],
        ["sections"] = [ViewType.Section],
        ["elevations"] = [ViewType.Elevation],
        ["3d"] = [ViewType.ThreeD],
        ["engineering_plans"] = [ViewType.EngineeringPlan],
        ["area_plans"] = [ViewType.AreaPlan],
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "preview";
        var targetType = parameters.GetValueOrDefault("target_type")?.ToString() ?? "";
        var operation = parameters.GetValueOrDefault("operation")?.ToString() ?? "add_prefix";
        var value = parameters.GetValueOrDefault("value")?.ToString();
        var replaceWith = parameters.GetValueOrDefault("replace_with")?.ToString() ?? "";
        var nameFilter = parameters.GetValueOrDefault("name_filter")?.ToString();
        var viewTypeFilter = parameters.GetValueOrDefault("view_type_filter")?.ToString();

        if (string.IsNullOrWhiteSpace(value))
            return SkillResult.Fail("Parameter 'value' is required.");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var items = CollectItems(document, targetType, nameFilter, viewTypeFilter);

            if (items.Count == 0)
                return new RenameResult { Error = $"No '{targetType}' items found matching criteria." };

            var changes = new List<RenameRecord>();
            var newNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                var oldName = item.CurrentName;
                var newName = ComputeNewName(oldName, operation, value!, replaceWith);
                if (newName == oldName || string.IsNullOrWhiteSpace(newName)) continue;

                if (RequiresUniqueName(targetType) && !newNames.Add(newName))
                {
                    var suffix = 2;
                    while (!newNames.Add($"{newName} ({suffix})")) suffix++;
                    newName = $"{newName} ({suffix})";
                }

                changes.Add(new RenameRecord
                {
                    ElementId = item.Id,
                    OldName = oldName,
                    NewName = newName,
                    ItemType = item.ItemType
                });
            }

            if (changes.Count == 0)
                return new RenameResult { Error = "No items would be changed by this operation." };

            if (action == "apply")
            {
                int renamed = 0, failed = 0;
                var failedNames = new List<string>();

                using var tx = new Transaction(document, $"Bulk rename {targetType}");
                tx.Start();

                foreach (var change in changes)
                {
                    try
                    {
                        var elem = document.GetElement(new ElementId(change.ElementId));
                        if (elem is null) { failed++; failedNames.Add(change.OldName); continue; }

                        if (IsRoomLikeElement(targetType))
                        {
                            var nameParam = elem.get_Parameter(BuiltInParameter.ROOM_NAME);
                            if (nameParam is not null && !nameParam.IsReadOnly)
                                nameParam.Set(change.NewName);
                            else
                            { failed++; failedNames.Add(change.OldName); continue; }
                        }
                        else
                        {
                            elem.Name = change.NewName;
                        }
                        renamed++;
                    }
                    catch
                    {
                        failed++;
                        failedNames.Add(change.OldName);
                    }
                }

                tx.Commit();

                return new RenameResult
                {
                    Success = true,
                    Action = "apply",
                    RenamedCount = renamed,
                    FailedCount = failed,
                    FailedNames = failedNames,
                    TotalProposed = changes.Count,
                    Details = changes.Take(50).ToList()
                };
            }

            return new RenameResult
            {
                Success = true,
                Action = "preview",
                TotalProposed = changes.Count,
                Details = changes.Take(50).ToList()
            };
        });

        var res = result as RenameResult;
        if (res is null || !res.Success)
            return SkillResult.Fail(res?.Error ?? "Bulk rename failed.");

        if (action == "apply")
            return SkillResult.Ok(
                $"Renamed {res.RenamedCount} {targetType}" +
                (res.FailedCount > 0 ? $" ({res.FailedCount} failed)" : "") + ".",
                result);

        return SkillResult.Ok(
            $"Preview: {res.TotalProposed} {targetType} would be renamed. Run with action='apply' to execute.",
            result);
    }

    private static List<NamedItem> CollectItems(Document doc, string targetType, string? nameFilter, string? viewTypeFilter)
    {
        var items = new List<NamedItem>();

        switch (targetType)
        {
            case "views":
            {
                var allowedViewTypes = !string.IsNullOrWhiteSpace(viewTypeFilter) && ViewTypeMap.TryGetValue(viewTypeFilter, out var vts)
                    ? new HashSet<ViewType>(vts) : null;

                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && v is not ViewSheet && v is not ViewSchedule);

                if (allowedViewTypes is not null)
                    views = views.Where(v => allowedViewTypes.Contains(v.ViewType));

                foreach (var v in views)
                    items.Add(new NamedItem(v.Id.Value, v.Name, v.ViewType.ToString()));
                break;
            }
            case "sheets":
            {
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder);

                foreach (var s in sheets)
                    items.Add(new NamedItem(s.Id.Value, s.Name, $"Sheet {s.SheetNumber}"));
                break;
            }
            case "rooms":
                items.AddRange(CollectRoomLike(doc, BuiltInCategory.OST_Rooms, "Room"));
                break;
            case "spaces":
                items.AddRange(CollectRoomLike(doc, BuiltInCategory.OST_MEPSpaces, "Space"));
                break;
            case "areas":
                items.AddRange(CollectRoomLike(doc, BuiltInCategory.OST_Areas, "Area"));
                break;
            case "levels":
            {
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>();
                foreach (var l in levels)
                    items.Add(new NamedItem(l.Id.Value, l.Name, "Level"));
                break;
            }
            case "grids":
            {
                var grids = new FilteredElementCollector(doc)
                    .OfClass(typeof(Grid))
                    .Cast<Grid>();
                foreach (var g in grids)
                    items.Add(new NamedItem(g.Id.Value, g.Name, "Grid"));
                break;
            }
            case "view_templates":
            {
                var templates = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v.IsTemplate);
                foreach (var vt in templates)
                    items.Add(new NamedItem(vt.Id.Value, vt.Name, "ViewTemplate"));
                break;
            }
            case "schedules":
            {
                var schedules = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(vs => !vs.IsTitleblockRevisionSchedule && !vs.IsInternalKeynoteSchedule);
                foreach (var sched in schedules)
                    items.Add(new NamedItem(sched.Id.Value, sched.Name, "Schedule"));
                break;
            }
        }

        if (!string.IsNullOrWhiteSpace(nameFilter))
            items = items.Where(i => i.CurrentName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        return items;
    }

    private static IEnumerable<NamedItem> CollectRoomLike(Document doc, BuiltInCategory bic, string itemType)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .Select(e =>
            {
                var name = e.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? e.Name;
                return new NamedItem(e.Id.Value, name, itemType);
            });
    }

    private static string ComputeNewName(string oldName, string operation, string value, string replaceWith)
    {
        return operation switch
        {
            "add_prefix" => value + oldName,
            "add_suffix" => oldName + value,
            "remove_prefix" => oldName.StartsWith(value, StringComparison.OrdinalIgnoreCase)
                ? oldName[value.Length..] : oldName,
            "remove_suffix" => oldName.EndsWith(value, StringComparison.OrdinalIgnoreCase)
                ? oldName[..^value.Length] : oldName,
            "find_replace" => oldName.Replace(value, replaceWith, StringComparison.OrdinalIgnoreCase),
            "regex_replace" => TryRegexReplace(oldName, value, replaceWith),
            _ => oldName
        };
    }

    private static string TryRegexReplace(string input, string pattern, string replacement)
    {
        try { return Regex.Replace(input, pattern, replacement, RegexOptions.IgnoreCase); }
        catch { return input; }
    }

    private static bool RequiresUniqueName(string targetType) =>
        targetType is "views" or "sheets" or "levels" or "grids" or "view_templates" or "schedules";

    private static bool IsRoomLikeElement(string targetType) =>
        targetType is "rooms" or "spaces" or "areas";

    private record NamedItem(long Id, string CurrentName, string ItemType);

    private class RenameResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string Action { get; set; } = "preview";
        public int RenamedCount { get; set; }
        public int FailedCount { get; set; }
        public List<string> FailedNames { get; set; } = [];
        public int TotalProposed { get; set; }
        public List<RenameRecord> Details { get; set; } = [];
    }

    private class RenameRecord
    {
        public long ElementId { get; set; }
        public string OldName { get; set; } = "";
        public string NewName { get; set; } = "";
        public string ItemType { get; set; } = "";
    }
}
