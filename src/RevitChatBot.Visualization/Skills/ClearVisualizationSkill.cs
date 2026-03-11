using RevitChatBot.Core.Skills;

namespace RevitChatBot.Visualization.Skills;

/// <summary>
/// Clears visualization overlays from the 3D view.
/// Can clear all, or only a specific tag (skill-specific highlights).
/// </summary>
[Skill("clear_visualization",
    "Clear visualization overlays from the 3D view. " +
    "Use to clean up after reviewing highlighted elements or clashes. " +
    "Can clear all visualizations or only a specific tag.")]
[SkillParameter("tag", "string",
    "Optional tag to clear only specific highlights (e.g., 'clash_viz', 'clearance_check'). " +
    "Leave empty to clear all visualizations.",
    isRequired: false)]
public class ClearVisualizationSkill : ISkill
{
    private readonly VisualizationManager _vizManager;

    public ClearVisualizationSkill(VisualizationManager vizManager)
    {
        _vizManager = vizManager;
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var tag = parameters.GetValueOrDefault("tag")?.ToString();
        int before = _vizManager.TotalGeometryCount;

        if (context.RevitApiInvoker is not null)
        {
            await context.RevitApiInvoker(_ =>
            {
                if (string.IsNullOrWhiteSpace(tag))
                    _vizManager.Clear();
                else
                    _vizManager.ClearByTag(tag);

                _vizManager.RefreshViews();
                return null;
            });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(tag))
                _vizManager.Clear();
            else
                _vizManager.ClearByTag(tag);
        }

        int after = _vizManager.TotalGeometryCount;
        int removed = before - after;

        var scope = string.IsNullOrWhiteSpace(tag) ? "all visualizations" : $"tag '{tag}'";
        return SkillResult.Ok(
            $"Cleared {scope}. Removed {removed} geometry items ({after} remaining).",
            new { cleared = removed, remaining = after, tag });
    }
}
