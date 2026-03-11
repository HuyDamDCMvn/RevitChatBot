using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Cleanup;

[Skill("purge_unused",
    "Comprehensive model purge — removes unused line styles, fill patterns, materials, " +
    "view templates, and filters. Complements cleanup_unused_families and cleanup_unused_views. " +
    "Run with action='audit' first to preview what will be removed.")]
[SkillParameter("action", "string",
    "'audit' to preview, 'delete' to actually purge.",
    isRequired: true,
    allowedValues: new[] { "audit", "delete" })]
[SkillParameter("target", "string",
    "What to purge: 'line_styles', 'filters', 'view_templates', 'materials', 'fill_patterns', or 'all'.",
    isRequired: false,
    allowedValues: new[] { "line_styles", "filters", "view_templates", "materials", "fill_patterns", "all" })]
public class PurgeUnusedSkill : ISkill
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

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var items = new List<PurgeItem>();

            if (target is "filters" or "all")
                items.AddRange(FindUnusedFilters(document));
            if (target is "view_templates" or "all")
                items.AddRange(FindUnusedViewTemplates(document));
            if (target is "materials" or "all")
                items.AddRange(FindUnusedMaterials(document));
            if (target is "fill_patterns" or "all")
                items.AddRange(FindUnusedFillPatterns(document));
            if (target is "line_styles" or "all")
                items.AddRange(FindUnusedLineStyles(document));

            if (action == "delete" && items.Count > 0)
            {
                var deleted = 0;
                var failed = 0;

                using var tx = new Transaction(document, "Purge unused model elements");
                tx.Start();

                foreach (var item in items)
                {
                    try
                    {
                        document.Delete(new ElementId(item.Id));
                        deleted++;
                    }
                    catch
                    {
                        failed++;
                    }
                }

                tx.Commit();

                return new
                {
                    action = "delete",
                    deletedCount = deleted,
                    failedCount = failed,
                    totalFound = items.Count,
                    details = items
                };
            }

            var grouped = items
                .GroupBy(i => i.ItemType)
                .Select(g => new { type = g.Key, count = g.Count() })
                .ToList();

            return new
            {
                action = "audit",
                deletedCount = 0,
                failedCount = 0,
                totalFound = items.Count,
                byType = grouped,
                details = items.Take(100).ToList()
            };
        });

        dynamic res = result!;
        if (action == "delete")
            return SkillResult.Ok(
                $"Purged {res.deletedCount} unused items ({res.failedCount} failed).", result);

        return SkillResult.Ok(
            $"Found {res.totalFound} unused purgeable items. Run with action='delete' to clean up.", result);
    }

    private static List<PurgeItem> FindUnusedFilters(Document doc)
    {
        var allViews = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate)
            .ToList();

        var allTemplates = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate)
            .ToList();

        var usedFilterIds = new HashSet<long>();
        foreach (var view in allViews.Concat(allTemplates))
        {
            try
            {
                foreach (var fId in view.GetFilters())
                    usedFilterIds.Add(fId.Value);
            }
            catch { /* view may not support filters */ }
        }

        return new FilteredElementCollector(doc)
            .OfClass(typeof(ParameterFilterElement))
            .Where(f => !usedFilterIds.Contains(f.Id.Value))
            .Select(f => new PurgeItem { Id = f.Id.Value, Name = f.Name, ItemType = "Filter" })
            .ToList();
    }

    private static List<PurgeItem> FindUnusedViewTemplates(Document doc)
    {
        var allViews = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate)
            .ToList();

        var usedTemplateIds = new HashSet<long>();
        foreach (var view in allViews)
        {
            if (view.ViewTemplateId != ElementId.InvalidElementId)
                usedTemplateIds.Add(view.ViewTemplateId.Value);
        }

        return new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate && !usedTemplateIds.Contains(v.Id.Value))
            .Select(v => new PurgeItem { Id = v.Id.Value, Name = v.Name, ItemType = "ViewTemplate" })
            .ToList();
    }

    private static List<PurgeItem> FindUnusedMaterials(Document doc)
    {
        var allElements = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .ToElements();

        var allTypes = new FilteredElementCollector(doc)
            .WhereElementIsElementType()
            .ToElements();

        var usedMaterialIds = new HashSet<long>();

        foreach (var elem in allElements.Concat(allTypes))
        {
            try
            {
                foreach (var matId in elem.GetMaterialIds(false))
                    usedMaterialIds.Add(matId.Value);
                foreach (var matId in elem.GetMaterialIds(true))
                    usedMaterialIds.Add(matId.Value);
            }
            catch { /* some elements don't support GetMaterialIds */ }
        }

        return new FilteredElementCollector(doc)
            .OfClass(typeof(Material))
            .Where(m => !usedMaterialIds.Contains(m.Id.Value))
            .Select(m => new PurgeItem { Id = m.Id.Value, Name = m.Name, ItemType = "Material" })
            .ToList();
    }

    private static List<PurgeItem> FindUnusedFillPatterns(Document doc)
    {
        var allElements = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .ToElements();

        var usedPatternIds = new HashSet<long>();
        foreach (var elem in allElements)
        {
            try
            {
                if (elem is FilledRegion fr)
                    usedPatternIds.Add(fr.GetTypeId().Value);
            }
            catch { }
        }

        return new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .Where(fp => !fp.GetFillPattern().IsSolidFill
                         && !usedPatternIds.Contains(fp.Id.Value))
            .Select(fp => new PurgeItem { Id = fp.Id.Value, Name = fp.Name, ItemType = "FillPattern" })
            .ToList();
    }

    private static List<PurgeItem> FindUnusedLineStyles(Document doc)
    {
        var items = new List<PurgeItem>();
        try
        {
            var lineCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (lineCategory?.SubCategories is null) return items;

            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements();

            var usedLineStyleIds = new HashSet<long>();
            foreach (var elem in allElements)
            {
                try
                {
                    var lineStyle = elem.Category;
                    if (lineStyle?.Parent?.Id.Value == lineCategory.Id.Value)
                        usedLineStyleIds.Add(lineStyle.Id.Value);
                }
                catch { }
            }

            foreach (Category subCat in lineCategory.SubCategories)
            {
                if (!usedLineStyleIds.Contains(subCat.Id.Value) && subCat.Id.Value > 0)
                {
                    items.Add(new PurgeItem
                    {
                        Id = subCat.Id.Value,
                        Name = subCat.Name,
                        ItemType = "LineStyle"
                    });
                }
            }
        }
        catch { }

        return items;
    }

    private class PurgeItem
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string ItemType { get; set; } = "";
    }
}
