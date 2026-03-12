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

    /// <summary>
    /// Invokes an action on the Revit main thread with UIDocument access.
    /// Required for UI operations such as setting selection, switching views, etc.
    /// The callback receives a UIDocument (boxed as object for Core project independence).
    /// </summary>
    public Func<Func<object, object?>, Task<object?>>? RevitUIInvoker { get; set; }

    public Dictionary<string, object?> Extra { get; set; } = new();

    /// <summary>
    /// Sends a BridgeMessage directly to the frontend UI.
    /// Skills can use this to push images, charts, or other rich content.
    /// </summary>
    public Action<string, string?, Dictionary<string, object?>?>? SendToUI { get; set; }

    public object? VisualizationManager
    {
        get => Extra.GetValueOrDefault("visualization_manager");
        set => Extra["visualization_manager"] = value;
    }
}
