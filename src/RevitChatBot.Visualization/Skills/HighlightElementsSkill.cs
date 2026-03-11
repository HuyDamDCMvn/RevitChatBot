using RevitChatBot.Core.Skills;

namespace RevitChatBot.Visualization.Skills;

/// <summary>
/// Agent-callable skill that highlights elements in the 3D view with
/// severity-based coloring. Works with element IDs from other skill results.
///
/// The LLM can chain this after any query/check skill to visually show results:
///   check_clearance → highlight_elements (red for violations)
///   query_elements → highlight_elements (blue for selection)
///   detect_clashes → highlight_elements (red for clashing pairs)
/// </summary>
[Skill("highlight_elements",
    "Highlight elements in the Revit 3D view with color-coded severity. " +
    "Use after query or check skills to visually show results to the user. " +
    "Colors: critical=red, warning=orange, ok=green, info=blue, clash=red transparent. " +
    "Pass element IDs from previous skill results. " +
    "Use clear_tag to remove previous highlights before adding new ones.")]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs to highlight (e.g., '123456,789012,345678')",
    isRequired: true)]
[SkillParameter("severity", "string",
    "Color severity: 'critical', 'warning', 'ok', 'info', 'clash', 'routing'",
    isRequired: false, allowedValues: new[] { "critical", "warning", "ok", "info", "clash", "routing" })]
[SkillParameter("clear_previous", "boolean",
    "Whether to clear previous highlights with the same tag before adding new ones. Default: true.",
    isRequired: false)]
[SkillParameter("tag", "string",
    "Tag for grouping highlights. Use skill name for easy cleanup (e.g., 'clearance_check'). Default: 'highlight'.",
    isRequired: false)]
public class HighlightElementsSkill : ISkill
{
    private readonly VisualizationManager _vizManager;

    public HighlightElementsSkill(VisualizationManager vizManager)
    {
        _vizManager = vizManager;
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var idsStr = parameters.GetValueOrDefault("element_ids")?.ToString();
        if (string.IsNullOrWhiteSpace(idsStr))
            return SkillResult.Fail("Parameter 'element_ids' is required.");

        var severity = parameters.GetValueOrDefault("severity")?.ToString() ?? "info";
        var tag = parameters.GetValueOrDefault("tag")?.ToString() ?? "highlight";
        var clearPrevious = parameters.GetValueOrDefault("clear_previous")?.ToString() != "false";

        if (!_vizManager.IsRegistered)
            return SkillResult.Fail("Visualization not available. No active 3D view.");

        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var result = await context.RevitApiInvoker(docObj =>
        {
            var doc = (Autodesk.Revit.DB.Document)docObj;
            if (clearPrevious)
                _vizManager.ClearByTag(tag);

            var ids = ParseElementIds(idsStr);
            int highlighted = 0;
            var notFound = new List<string>();

            foreach (var id in ids)
            {
                var element = doc.GetElement(new Autodesk.Revit.DB.ElementId(id));
                if (element is not null)
                {
                    _vizManager.HighlightElement(element, severity, tag);
                    highlighted++;
                }
                else
                {
                    notFound.Add(id.ToString());
                }
            }

            _vizManager.RefreshViews();

            return new { highlighted, notFound, total = ids.Count };
        });

        if (result is null)
            return SkillResult.Fail("Failed to highlight elements.");

        dynamic r = result;
        int count = r.highlighted;
        List<string> missing = r.notFound;
        int total = r.total;

        var msg = $"Highlighted {count}/{total} elements with '{severity}' style (tag: '{tag}').";
        if (missing.Count > 0)
            msg += $" Not found: {string.Join(", ", missing.Take(5))}" +
                   (missing.Count > 5 ? $" +{missing.Count - 5} more" : "");

        return SkillResult.Ok(msg, new
        {
            highlighted = count,
            severity,
            tag,
            notFound = missing
        });
    }

    private static List<long> ParseElementIds(string idsStr)
    {
        return idsStr
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s.Trim(), out var id) ? id : -1)
            .Where(id => id > 0)
            .ToList();
    }
}
