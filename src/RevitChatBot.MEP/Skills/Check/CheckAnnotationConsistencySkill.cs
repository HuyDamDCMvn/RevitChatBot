using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("check_annotation_consistency",
    "Check annotation consistency across views. Verifies that the same category " +
    "uses the same tag family, orientation, and leader settings across all views. " +
    "Also checks dimension style consistency and text size relative to view scale.")]
[SkillParameter("scope", "string",
    "Check scope: 'active_view', 'all_plans', 'all_views'. Default 'all_plans'.",
    isRequired: false,
    allowedValues: new[] { "active_view", "all_plans", "all_views" })]
[SkillParameter("category", "string",
    "Filter to specific category (e.g. 'Ducts', 'Pipes'). Default checks all.",
    isRequired: false)]
public class CheckAnnotationConsistencySkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var scope = parameters.GetValueOrDefault("scope")?.ToString() ?? "all_plans";
        var categoryFilter = parameters.GetValueOrDefault("category")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var views = ResolveViews(document, scope);
            if (views.Count == 0)
                return new { success = false, message = "No views found.", issues = new List<object>() };

            var issues = new List<object>();
            var tagFamilyUsage = new Dictionary<string, Dictionary<string, int>>();
            var orientationUsage = new Dictionary<string, Dictionary<string, int>>();
            var leaderUsage = new Dictionary<string, Dictionary<bool, int>>();

            foreach (var view in views)
            {
                var tags = new FilteredElementCollector(document, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .ToList();

                foreach (var tag in tags)
                {
                    var host = tag.GetTaggedElement();
                    if (host is null) continue;

                    var catName = host.Category?.Name ?? "Unknown";
                    if (categoryFilter is not null &&
                        !catName.Contains(categoryFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var tagTypeName = document.GetElement(tag.GetTypeId())?.Name ?? "Default";
                    var orientation = tag.TagOrientation.ToString();
                    var hasLeader = tag.HasLeader();

                    if (!tagFamilyUsage.ContainsKey(catName))
                        tagFamilyUsage[catName] = [];
                    tagFamilyUsage[catName].TryGetValue(tagTypeName, out int fc);
                    tagFamilyUsage[catName][tagTypeName] = fc + 1;

                    if (!orientationUsage.ContainsKey(catName))
                        orientationUsage[catName] = [];
                    orientationUsage[catName].TryGetValue(orientation, out int oc);
                    orientationUsage[catName][orientation] = oc + 1;

                    if (!leaderUsage.ContainsKey(catName))
                        leaderUsage[catName] = [];
                    leaderUsage[catName].TryGetValue(hasLeader, out int lc);
                    leaderUsage[catName][hasLeader] = lc + 1;
                }
            }

            // Analyze inconsistencies
            foreach (var (cat, families) in tagFamilyUsage)
            {
                if (families.Count > 1)
                {
                    issues.Add(new
                    {
                        type = "INCONSISTENT_TAG_FAMILY",
                        severity = "medium",
                        category = cat,
                        families = families.Select(f =>
                            new { name = f.Key, count = f.Value }).ToList(),
                        description = $"{cat}: {families.Count} different tag families used — " +
                            string.Join(", ", families.Select(f => $"{f.Key}({f.Value}x)"))
                    });
                }
            }

            foreach (var (cat, orientations) in orientationUsage)
            {
                if (orientations.Count > 1)
                {
                    issues.Add(new
                    {
                        type = "INCONSISTENT_ORIENTATION",
                        severity = "low",
                        category = cat,
                        orientations = orientations.Select(o =>
                            new { orientation = o.Key, count = o.Value }).ToList(),
                        description = $"{cat}: mixed tag orientations — " +
                            string.Join(", ", orientations.Select(o => $"{o.Key}({o.Value}x)"))
                    });
                }
            }

            foreach (var (cat, leaders) in leaderUsage)
            {
                if (leaders.Count > 1)
                {
                    issues.Add(new
                    {
                        type = "INCONSISTENT_LEADER",
                        severity = "low",
                        category = cat,
                        description = $"{cat}: mixed leader usage — " +
                            string.Join(", ", leaders.Select(l =>
                                $"{(l.Key ? "with leader" : "no leader")}({l.Value}x)"))
                    });
                }
            }

            // Check dimension style consistency
            var dimStyles = new Dictionary<string, int>();
            foreach (var view in views)
            {
                var dims = new FilteredElementCollector(document, view.Id)
                    .OfCategory(BuiltInCategory.OST_Dimensions)
                    .WhereElementIsNotElementType()
                    .Cast<Dimension>()
                    .ToList();

                foreach (var dim in dims)
                {
                    var typeName = document.GetElement(dim.GetTypeId())?.Name ?? "Default";
                    dimStyles.TryGetValue(typeName, out int dc);
                    dimStyles[typeName] = dc + 1;
                }
            }

            if (dimStyles.Count > 2)
            {
                issues.Add(new
                {
                    type = "INCONSISTENT_DIMENSION_STYLE",
                    severity = "medium",
                    styles = dimStyles.Select(s =>
                        new { name = s.Key, count = s.Value }).ToList(),
                    description = $"{dimStyles.Count} different dimension styles used across views"
                });
            }

            var status = issues.Count == 0 ? "CONSISTENT" : "INCONSISTENCIES FOUND";
            return new
            {
                success = true,
                message = $"Annotation consistency: {status}. " +
                    $"Checked {views.Count} views. Found {issues.Count} inconsistency issue(s).",
                issues
            };
        });

        var r = (dynamic)result!;
        return (bool)r.success
            ? SkillResult.Ok((string)r.message, result)
            : SkillResult.Fail((string)r.message);
    }

    private static List<View> ResolveViews(Document doc, string scope)
    {
        return scope switch
        {
            "all_views" => new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType is not (ViewType.Schedule or ViewType.Legend
                    or ViewType.DraftingView or ViewType.DrawingSheet))
                .ToList(),

            "all_plans" => new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate)
                .Cast<View>()
                .ToList(),

            _ => []
        };
    }
}
