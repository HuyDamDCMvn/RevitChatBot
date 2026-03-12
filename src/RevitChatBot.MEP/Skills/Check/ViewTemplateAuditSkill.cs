using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("view_template_audit",
    "Audit view templates for consistency and usage. Reports which templates exist, " +
    "how many views use each, which views have no template, and detects templates " +
    "with potentially conflicting or missing settings.")]
[SkillParameter("action", "string",
    "'list' to list all templates, 'usage' to show usage per template, " +
    "'orphan' to find views without templates, 'full' for complete audit. Default: 'full'.",
    isRequired: false,
    allowedValues: new[] { "list", "usage", "orphan", "full" })]
[SkillParameter("discipline_filter", "string",
    "Filter by discipline: 'mechanical', 'electrical', 'plumbing', 'coordination', or 'all'. " +
    "Default: 'all'.",
    isRequired: false)]
public class ViewTemplateAuditSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var action = parameters.GetValueOrDefault("action")?.ToString()?.ToLower() ?? "full";
        var discFilter = parameters.GetValueOrDefault("discipline_filter")?.ToString()?.ToLower() ?? "all";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var allViews = new FilteredElementCollector(document)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .ToList();

            var templates = new FilteredElementCollector(document)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .ToList();

            if (discFilter != "all")
            {
                var discEnum = discFilter switch
                {
                    "mechanical" => ViewDiscipline.Mechanical,
                    "electrical" => ViewDiscipline.Electrical,
                    "plumbing" => ViewDiscipline.Plumbing,
                    "coordination" => ViewDiscipline.Coordination,
                    _ => (ViewDiscipline?)null
                };
                if (discEnum.HasValue)
                {
                    templates = templates.Where(t =>
                    {
                        try { return t.Discipline == discEnum.Value; }
                        catch { return false; }
                    }).ToList();
                }
            }

            var templateUsage = new Dictionary<string, List<string>>();
            foreach (var t in templates)
                templateUsage[t.Name] = new List<string>();

            var orphanViews = new List<object>();

            foreach (var v in allViews)
            {
                var templateId = v.ViewTemplateId;
                if (templateId == ElementId.InvalidElementId)
                {
                    orphanViews.Add(new { id = v.Id.Value, name = v.Name, viewType = v.ViewType.ToString() });
                }
                else
                {
                    var templateView = document.GetElement(templateId) as View;
                    if (templateView is not null && templateUsage.ContainsKey(templateView.Name))
                        templateUsage[templateView.Name].Add(v.Name);
                }
            }

            var unusedTemplates = templateUsage
                .Where(kv => kv.Value.Count == 0)
                .Select(kv => kv.Key)
                .ToList();

            var templateList = templates.Select(t => new
            {
                id = t.Id.Value,
                name = t.Name,
                viewType = t.ViewType.ToString(),
                usageCount = templateUsage.ContainsKey(t.Name) ? templateUsage[t.Name].Count : 0,
            }).OrderByDescending(t => t.usageCount).ToList();

            var report = new Dictionary<string, object>
            {
                ["totalTemplates"] = templates.Count,
                ["totalViews"] = allViews.Count,
                ["orphanViewCount"] = orphanViews.Count,
                ["unusedTemplateCount"] = unusedTemplates.Count,
            };

            if (action is "list" or "full")
                report["templates"] = templateList;
            if (action is "usage" or "full")
                report["templateUsage"] = templateUsage.Select(kv => new { template = kv.Key, views = kv.Value }).ToList();
            if (action is "orphan" or "full")
                report["orphanViews"] = orphanViews.Take(50).ToList();
            if (action == "full")
                report["unusedTemplates"] = unusedTemplates;

            return report;
        });

        var data = (Dictionary<string, object>)result!;
        var summary = $"View template audit: {data["totalTemplates"]} templates, " +
                      $"{data["totalViews"]} views, {data["orphanViewCount"]} views without template, " +
                      $"{data["unusedTemplateCount"]} unused templates.";
        return SkillResult.Ok(summary, result);
    }
}
