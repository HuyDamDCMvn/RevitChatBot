namespace RevitChatBot.Core.Skills;

public interface ISkill
{
    Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);
}

public class SkillContext
{
    public object? RevitDocument { get; set; }
    public Func<Func<object, object?>, Task<object?>>? RevitApiInvoker { get; set; }
    public Dictionary<string, object?> Extra { get; set; } = new();

    /// <summary>
    /// Visualization manager for DirectContext3D rendering.
    /// Skills can use this to highlight elements, show clashes, or visualize routes.
    /// Null when visualization is not available (no active 3D view).
    /// Access via Extra["visualization_manager"] for loose coupling, or cast directly.
    /// </summary>
    public object? VisualizationManager
    {
        get => Extra.GetValueOrDefault("visualization_manager");
        set => Extra["visualization_manager"] = value;
    }
}
