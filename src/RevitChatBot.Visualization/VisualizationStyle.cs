using Autodesk.Revit.DB;

namespace RevitChatBot.Visualization;

/// <summary>
/// Defines visual appearance for rendered geometry.
/// Semantic colors allow the agent to communicate issue severity visually:
/// - Red = critical issue (clash, fire damper missing)
/// - Orange = warning (velocity too high, missing insulation)
/// - Green = ok / passed check
/// - Blue = informational / highlight
/// - Cyan = routing path / system trace
/// </summary>
public class VisualizationStyle
{
    public Color Color { get; init; } = new(255, 0, 0);
    public byte Transparency { get; init; } = 80;
    public double LineWeight { get; init; } = 2.0;

    public ColorWithTransparency ToColorWithTransparency() =>
        new(Color.Red, Color.Green, Color.Blue, Transparency);

    // --- Semantic presets for MEP QA/QC ---

    public static VisualizationStyle Default { get; } = new()
    {
        Color = new Color(0, 120, 255), Transparency = 100
    };

    public static VisualizationStyle Critical { get; } = new()
    {
        Color = new Color(255, 30, 30), Transparency = 60
    };

    public static VisualizationStyle Warning { get; } = new()
    {
        Color = new Color(255, 165, 0), Transparency = 80
    };

    public static VisualizationStyle Ok { get; } = new()
    {
        Color = new Color(30, 200, 30), Transparency = 100
    };

    public static VisualizationStyle Info { get; } = new()
    {
        Color = new Color(0, 120, 255), Transparency = 100
    };

    public static VisualizationStyle RoutingPath { get; } = new()
    {
        Color = new Color(0, 200, 200), Transparency = 60
    };

    public static VisualizationStyle Clash { get; } = new()
    {
        Color = new Color(255, 0, 0), Transparency = 40
    };

    public static VisualizationStyle Selection { get; } = new()
    {
        Color = new Color(255, 255, 0), Transparency = 80
    };

    public static VisualizationStyle FromSeverity(string severity) => severity.ToLowerInvariant() switch
    {
        "critical" => Critical,
        "major" or "warning" => Warning,
        "minor" or "info" => Info,
        "ok" or "pass" => Ok,
        "clash" => Clash,
        "route" or "routing" or "path" => RoutingPath,
        _ => Default
    };
}
