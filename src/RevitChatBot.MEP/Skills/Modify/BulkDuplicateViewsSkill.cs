using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("bulk_duplicate_views",
    "Duplicate multiple views, sheets, or schedules at once. " +
    "Supports three duplication modes for views: structure only, with detailing, or as dependent. " +
    "Optionally renames duplicated items with prefix/suffix. " +
    "Run with action='preview' to see what will be duplicated.")]
[SkillParameter("action", "string",
    "'preview' to list what will be duplicated, 'apply' to create copies.",
    isRequired: true,
    allowedValues: new[] { "preview", "apply" })]
[SkillParameter("target_type", "string",
    "What to duplicate: 'views', 'sheets', 'schedules'.",
    isRequired: true,
    allowedValues: new[] { "views", "sheets", "schedules" })]
[SkillParameter("name_filter", "string",
    "Only duplicate items whose name contains this text (e.g. 'MEP', 'M-', 'Level 1').",
    isRequired: false)]
[SkillParameter("view_type_filter", "string",
    "For views: 'floor_plans', 'sections', 'elevations', '3d', 'ceiling_plans', 'engineering_plans'.",
    isRequired: false)]
[SkillParameter("duplicate_mode", "string",
    "For views: 'without_detailing' (structure only), 'with_detailing' (includes annotations), " +
    "'as_dependent' (linked to parent). Default: 'with_detailing'.",
    isRequired: false,
    allowedValues: new[] { "without_detailing", "with_detailing", "as_dependent" })]
[SkillParameter("rename_prefix", "string",
    "Prefix to add to duplicated item names (e.g. 'Rev2_', 'Copy_').",
    isRequired: false)]
[SkillParameter("rename_suffix", "string",
    "Suffix to add to duplicated item names (e.g. ' - Phase 2', ' (Copy)').",
    isRequired: false)]
public class BulkDuplicateViewsSkill : ISkill
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
        var targetType = parameters.GetValueOrDefault("target_type")?.ToString() ?? "views";
        var nameFilter = parameters.GetValueOrDefault("name_filter")?.ToString();
        var viewTypeFilter = parameters.GetValueOrDefault("view_type_filter")?.ToString();
        var duplicateMode = parameters.GetValueOrDefault("duplicate_mode")?.ToString() ?? "with_detailing";
        var renamePrefix = parameters.GetValueOrDefault("rename_prefix")?.ToString() ?? "";
        var renameSuffix = parameters.GetValueOrDefault("rename_suffix")?.ToString() ?? "";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var sources = CollectSources(document, targetType, nameFilter, viewTypeFilter);

            if (sources.Count == 0)
                return new DuplicateResult { Error = $"No '{targetType}' items found matching criteria." };

            var plan = sources.Select(s =>
            {
                var newName = ComputeNewName(s.Name, renamePrefix, renameSuffix);
                return new DuplicatePlanItem
                {
                    SourceId = s.Id.Value,
                    SourceName = s.Name,
                    NewName = newName,
                    ItemType = s.ViewTypeName
                };
            }).ToList();

            if (action != "apply")
            {
                return new DuplicateResult
                {
                    Success = true,
                    Action = "preview",
                    TotalPlanned = plan.Count,
                    Details = plan.Take(50).ToList()
                };
            }

            var dupOption = ParseDuplicateOption(duplicateMode);
            int duplicated = 0, failed = 0;
            var failedNames = new List<string>();
            var created = new List<DuplicatePlanItem>();

            using var tx = new Transaction(document, $"Bulk duplicate {targetType}");
            tx.Start();

            foreach (var item in plan)
            {
                try
                {
                    var source = document.GetElement(new ElementId(item.SourceId));
                    if (source is null) { failed++; failedNames.Add(item.SourceName); continue; }

                    ElementId newId;
                    switch (targetType)
                    {
                        case "views":
                            if (source is not View sourceView) { failed++; failedNames.Add(item.SourceName); continue; }
                            newId = sourceView.Duplicate(dupOption);
                            break;

                        case "sheets":
                            if (source is not ViewSheet sourceSheet) { failed++; failedNames.Add(item.SourceName); continue; }
                            newId = DuplicateSheet(document, sourceSheet);
                            break;

                        case "schedules":
                            if (source is not ViewSchedule sourceSchedule) { failed++; failedNames.Add(item.SourceName); continue; }
                            newId = sourceSchedule.Duplicate(ViewDuplicateOption.Duplicate);
                            break;

                        default:
                            failed++; failedNames.Add(item.SourceName); continue;
                    }

                    if (newId == ElementId.InvalidElementId)
                    {
                        failed++;
                        failedNames.Add(item.SourceName);
                        continue;
                    }

                    var newElem = document.GetElement(newId);
                    if (newElem is not null)
                    {
                        var finalName = EnsureUniqueName(document, item.NewName, targetType);
                        try
                        {
                            if (newElem is ViewSheet newSheet)
                                newSheet.Name = finalName;
                            else
                                newElem.Name = finalName;
                        }
                        catch { /* keep auto-generated name */ }

                        created.Add(new DuplicatePlanItem
                        {
                            SourceId = item.SourceId,
                            SourceName = item.SourceName,
                            NewName = newElem.Name,
                            NewId = newId.Value,
                            ItemType = item.ItemType
                        });
                    }

                    duplicated++;
                }
                catch
                {
                    failed++;
                    failedNames.Add(item.SourceName);
                }
            }

            tx.Commit();

            return new DuplicateResult
            {
                Success = true,
                Action = "apply",
                DuplicatedCount = duplicated,
                FailedCount = failed,
                FailedNames = failedNames,
                TotalPlanned = plan.Count,
                Details = created.Take(50).ToList()
            };
        });

        var res = result as DuplicateResult;
        if (res is null || !res.Success)
            return SkillResult.Fail(res?.Error ?? "Bulk duplicate failed.");

        if (action == "apply")
            return SkillResult.Ok(
                $"Duplicated {res.DuplicatedCount} {targetType}" +
                (res.FailedCount > 0 ? $" ({res.FailedCount} failed)" : "") + ".",
                result);

        return SkillResult.Ok(
            $"Preview: {res.TotalPlanned} {targetType} will be duplicated. Run with action='apply' to execute.",
            result);
    }

    private static List<SourceItem> CollectSources(Document doc, string targetType, string? nameFilter, string? viewTypeFilter)
    {
        var items = new List<SourceItem>();

        switch (targetType)
        {
            case "views":
            {
                var allowedTypes = !string.IsNullOrWhiteSpace(viewTypeFilter)
                    && ViewTypeMap.TryGetValue(viewTypeFilter, out var vts)
                    ? new HashSet<ViewType>(vts) : null;

                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && v is not ViewSheet && v is not ViewSchedule
                                && v.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing));

                if (allowedTypes is not null)
                    views = views.Where(v => allowedTypes.Contains(v.ViewType));

                foreach (var v in views)
                    items.Add(new SourceItem(v.Id, v.Name, v.ViewType.ToString()));
                break;
            }
            case "sheets":
            {
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder);
                foreach (var s in sheets)
                    items.Add(new SourceItem(s.Id, s.Name, $"Sheet {s.SheetNumber}"));
                break;
            }
            case "schedules":
            {
                var scheds = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(vs => !vs.IsTitleblockRevisionSchedule && !vs.IsInternalKeynoteSchedule);
                foreach (var sched in scheds)
                    items.Add(new SourceItem(sched.Id, sched.Name, "Schedule"));
                break;
            }
        }

        if (!string.IsNullOrWhiteSpace(nameFilter))
            items = items.Where(i => i.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        return items;
    }

    private static ElementId DuplicateSheet(Document doc, ViewSheet sourceSheet)
    {
        var titleblockId = GetTitleblockTypeId(doc, sourceSheet);
        if (titleblockId == ElementId.InvalidElementId)
            return ElementId.InvalidElementId;

        var newSheet = ViewSheet.Create(doc, titleblockId);

        try
        {
            var viewportIds = sourceSheet.GetAllPlacedViews();
            foreach (var vpViewId in viewportIds)
            {
                var viewports = new FilteredElementCollector(doc)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>()
                    .Where(vp => vp.SheetId == sourceSheet.Id && vp.ViewId == vpViewId)
                    .ToList();

                foreach (var vp in viewports)
                {
                    var center = vp.GetBoxCenter();
                    try
                    {
                        Viewport.Create(doc, newSheet.Id, vpViewId, center);
                    }
                    catch { /* view may already be placed on another sheet */ }
                }
            }
        }
        catch { }

        return newSheet.Id;
    }

    private static ElementId GetTitleblockTypeId(Document doc, ViewSheet sheet)
    {
        var titleblocks = new FilteredElementCollector(doc, sheet.Id)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsNotElementType()
            .ToList();

        if (titleblocks.Count > 0)
            return titleblocks[0].GetTypeId();

        return new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsElementType()
            .FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
    }

    private static string ComputeNewName(string sourceName, string prefix, string suffix)
    {
        var newName = prefix + sourceName + suffix;
        return newName == sourceName ? sourceName + " Copy" : newName;
    }

    private static string EnsureUniqueName(Document doc, string baseName, string targetType)
    {
        var existingNames = targetType switch
        {
            "views" => new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .Select(v => v.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            "sheets" => new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Select(s => s.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            "schedules" => new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Select(s => s.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            _ => new HashSet<string>()
        };

        if (!existingNames.Contains(baseName)) return baseName;

        for (int i = 2; i < 1000; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!existingNames.Contains(candidate)) return candidate;
        }
        return $"{baseName}_{Guid.NewGuid().ToString()[..6]}";
    }

    private static ViewDuplicateOption ParseDuplicateOption(string mode) => mode switch
    {
        "without_detailing" => ViewDuplicateOption.Duplicate,
        "as_dependent" => ViewDuplicateOption.AsDependent,
        _ => ViewDuplicateOption.WithDetailing,
    };

    private record SourceItem(ElementId Id, string Name, string ViewTypeName);

    private class DuplicateResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string Action { get; set; } = "preview";
        public int DuplicatedCount { get; set; }
        public int FailedCount { get; set; }
        public List<string> FailedNames { get; set; } = [];
        public int TotalPlanned { get; set; }
        public List<DuplicatePlanItem> Details { get; set; } = [];
    }

    private class DuplicatePlanItem
    {
        public long SourceId { get; set; }
        public string SourceName { get; set; } = "";
        public string NewName { get; set; } = "";
        public long NewId { get; set; }
        public string ItemType { get; set; } = "";
    }
}
