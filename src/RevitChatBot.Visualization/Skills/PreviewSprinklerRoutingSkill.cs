using RevitChatBot.Core.Skills;

namespace RevitChatBot.Visualization.Skills;

/// <summary>
/// Renders or clears a sprinkler routing preview from route data stored
/// in SkillContext.Extra by AutoConnectSprinklerSkill.
///
/// Chaining:  auto_connect_sprinklers(mode=preview) → this skill auto-renders.
/// This skill is useful when the agent needs to re-render or clear independently.
/// </summary>
[Skill("preview_sprinkler_routing",
    "Render or clear sprinkler routing preview in the 3D view. " +
    "Reads route data from the last auto_connect_sprinklers preview. " +
    "Mode 'show' renders the preview, 'clear' removes it.")]
[SkillParameter("mode", "string",
    "Operation: 'show' to render preview, 'clear' to remove it. Default: 'show'.",
    isRequired: false,
    allowedValues: new[] { "show", "clear" })]
public class PreviewSprinklerRoutingSkill : ISkill
{
    private readonly VisualizationManager _vizManager;
    private const string RouteDataKey = "sprinkler_route_data";
    private const string PreviewTag = "sprinkler_preview";

    public PreviewSprinklerRoutingSkill(VisualizationManager vizManager)
    {
        _vizManager = vizManager;
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var mode = parameters.GetValueOrDefault("mode")?.ToString() ?? "show";

        if (mode == "clear")
        {
            _vizManager.ClearSprinklerPreview(PreviewTag);

            if (context.RevitApiInvoker is not null)
            {
                await context.RevitApiInvoker(_ =>
                {
                    _vizManager.RefreshViews();
                    return null;
                });
            }

            return SkillResult.Ok("Sprinkler routing preview cleared.");
        }

        if (!_vizManager.IsRegistered)
            return SkillResult.Fail("Visualization not available. Open a 3D view first.");

        var routeData = context.Extra.GetValueOrDefault(RouteDataKey) as SprinklerRouteData;
        if (routeData is null || routeData.TotalHeads == 0)
            return SkillResult.Fail(
                "No sprinkler route data available. " +
                "Run 'auto_connect_sprinklers' with mode='preview' first.");

        int rendered = _vizManager.PreviewSprinklerRoute(routeData, PreviewTag);

        if (context.RevitApiInvoker is not null)
        {
            await context.RevitApiInvoker(_ =>
            {
                _vizManager.RefreshViews();
                return null;
            });
        }

        return SkillResult.Ok(
            $"Sprinkler routing preview rendered: " +
            $"{routeData.TotalHeads} heads, {routeData.TotalSegments} segments, " +
            $"{routeData.TotalFittings} fittings. " +
            $"Total length: {Math.Round(routeData.TotalLengthFeet * 0.3048, 1)}m.",
            new
            {
                routeData.TotalHeads,
                routeData.TotalSegments,
                routeData.TotalFittings,
                totalLengthM = Math.Round(routeData.TotalLengthFeet * 0.3048, 1),
                rendered
            });
    }
}
