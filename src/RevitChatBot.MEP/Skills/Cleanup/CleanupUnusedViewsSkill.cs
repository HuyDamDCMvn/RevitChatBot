using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Cleanup;

[Skill("cleanup_unused_views",
    "Audit and optionally delete unused views, schedules, and sheets from the Revit model. " +
    "First run with action='audit' to preview, then 'delete' to clean up. " +
    "Preserves views placed on sheets, the starting view, and view templates.")]
[SkillParameter("action", "string",
    "'audit' to list unused views without deleting, 'delete' to actually remove them.",
    isRequired: true,
    allowedValues: new[] { "audit", "delete" })]
[SkillParameter("target", "string",
    "What to clean: 'views' (floor plans, sections, elevations, 3D), 'schedules', 'sheets', or 'all'.",
    isRequired: false,
    allowedValues: new[] { "views", "schedules", "sheets", "all" })]
[SkillParameter("name_filter", "string",
    "Optional name pattern to limit scope (e.g. 'Copy of' to only target copies).",
    isRequired: false)]
public class CleanupUnusedViewsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "audit";
        var target = parameters.GetValueOrDefault("target")?.ToString() ?? "all";
        var nameFilter = parameters.GetValueOrDefault("name_filter")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var viewsOnSheets = GetViewIdsPlacedOnSheets(document);
            var startingViewId = GetStartingViewId(document);

            var unusedViews = new List<ViewInfo>();
            var unusedSchedules = new List<ViewInfo>();
            var unusedSheets = new List<ViewInfo>();

            if (target is "views" or "all")
                unusedViews = FindUnusedViews(document, viewsOnSheets, startingViewId, nameFilter);

            if (target is "schedules" or "all")
                unusedSchedules = FindUnusedSchedules(document, viewsOnSheets, nameFilter);

            if (target is "sheets" or "all")
                unusedSheets = FindEmptySheets(document, nameFilter);

            var allUnused = unusedViews.Concat(unusedSchedules).Concat(unusedSheets).ToList();

            if (action == "delete" && allUnused.Count > 0)
            {
                var deletedCount = 0;
                var failedCount = 0;
                var failedNames = new List<string>();

                using var tx = new Transaction(document, "Cleanup unused views");
                tx.Start();

                foreach (var info in allUnused)
                {
                    try
                    {
                        document.Delete(new ElementId(info.Id));
                        deletedCount++;
                    }
                    catch
                    {
                        failedCount++;
                        failedNames.Add(info.Name);
                    }
                }

                tx.Commit();

                return new
                {
                    action = "delete",
                    deletedCount,
                    failedCount,
                    failedNames,
                    details = allUnused
                };
            }

            return new
            {
                action = "audit",
                deletedCount = 0,
                failedCount = 0,
                failedNames = new List<string>(),
                unusedViewsCount = unusedViews.Count,
                unusedSchedulesCount = unusedSchedules.Count,
                unusedSheetsCount = unusedSheets.Count,
                totalUnused = allUnused.Count,
                details = allUnused
            };
        });

        dynamic res = result!;
        if (action == "delete")
            return SkillResult.Ok(
                $"Deleted {res.deletedCount} unused items ({res.failedCount} failed).", result);

        return SkillResult.Ok(
            $"Audit found {res.totalUnused} unused items. Run with action='delete' to remove them.", result);
    }

    private static HashSet<long> GetViewIdsPlacedOnSheets(Document doc)
    {
        var ids = new HashSet<long>();
        var sheets = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>();

        foreach (var sheet in sheets)
        {
            foreach (var vpId in sheet.GetAllPlacedViews())
                ids.Add(vpId.Value);
            ids.Add(sheet.Id.Value);
        }

        return ids;
    }

    private static long GetStartingViewId(Document doc)
    {
        var startView = new FilteredElementCollector(doc)
            .OfClass(typeof(StartingViewSettings))
            .FirstOrDefault() as StartingViewSettings;
        return startView?.ViewId.Value ?? -1;
    }

    private static List<ViewInfo> FindUnusedViews(
        Document doc, HashSet<long> viewsOnSheets, long startingViewId, string? nameFilter)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate
                        && v is not ViewSheet
                        && v is not ViewSchedule
                        && !viewsOnSheets.Contains(v.Id.Value)
                        && v.Id.Value != startingViewId
                        && v.Id.Value != doc.ActiveView.Id.Value
                        && MatchesNameFilter(v.Name, nameFilter))
            .Select(v => new ViewInfo
            {
                Id = v.Id.Value,
                Name = v.Name,
                ViewType = v.ViewType.ToString(),
                Category = "View"
            })
            .ToList();
    }

    private static List<ViewInfo> FindUnusedSchedules(
        Document doc, HashSet<long> viewsOnSheets, string? nameFilter)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(vs => !vs.IsTitleblockRevisionSchedule
                         && !vs.IsInternalKeynoteSchedule
                         && !viewsOnSheets.Contains(vs.Id.Value)
                         && MatchesNameFilter(vs.Name, nameFilter))
            .Select(vs => new ViewInfo
            {
                Id = vs.Id.Value,
                Name = vs.Name,
                ViewType = "Schedule",
                Category = "Schedule"
            })
            .ToList();
    }

    private static List<ViewInfo> FindEmptySheets(Document doc, string? nameFilter)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Where(s => s.GetAllPlacedViews().Count == 0
                        && MatchesNameFilter(s.Name, nameFilter))
            .Select(s => new ViewInfo
            {
                Id = s.Id.Value,
                Name = $"{s.SheetNumber} - {s.Name}",
                ViewType = "Sheet",
                Category = "Sheet"
            })
            .ToList();
    }

    private static bool MatchesNameFilter(string name, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        return name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private class ViewInfo
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string ViewType { get; set; } = "";
        public string Category { get; set; } = "";
    }
}
