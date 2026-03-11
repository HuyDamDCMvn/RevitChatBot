using RevitChatBot.Core.Context;

namespace RevitChatBot.Visualization.Context;

/// <summary>
/// Provides visualization state context to the agent's prompt.
/// The agent can see what's currently being displayed in the 3D view
/// and decide whether to add more, clear, or reference existing visualizations.
/// This enables the self-learning loop: the agent learns which visual feedback
/// patterns are most helpful for different MEP tasks.
/// </summary>
public class VisualizationContextProvider : IContextProvider
{
    private readonly VisualizationManager _vizManager;

    public VisualizationContextProvider(VisualizationManager vizManager)
    {
        _vizManager = vizManager;
    }

    public string Name => "visualization_state";
    public int Priority => 90;
    public bool NeedsRevitApi => false;

    public Task<ContextData> GatherAsync(object? revitDocument = null)
    {
        var data = new ContextData();

        if (!_vizManager.IsRegistered)
        {
            data.Add("visualization_state",
                "3D visualization: NOT AVAILABLE (no active document/view registered).");
            return Task.FromResult(data);
        }

        var summary = _vizManager.GetVisualizationSummary();
        var records = _vizManager.GetRecentRecords(20);

        var contextStr = summary;
        if (records.Count > 0)
        {
            var recentActions = string.Join("\n", records.TakeLast(10).Select(r =>
                $"  [{r.Timestamp:HH:mm:ss}] {r.GeometryType} tag={r.Tag ?? "none"} style={r.StyleName}" +
                (r.Detail is not null ? $" ({r.Detail})" : "")));
            contextStr += $"\nRecent visualization actions:\n{recentActions}";
        }

        data.Add("visualization_state", contextStr);
        return Task.FromResult(data);
    }
}
