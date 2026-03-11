namespace RevitChatBot.Core.Agent;

/// <summary>
/// Structured action plan for destructive operations, shown to the user before execution.
/// The plan_and_approve flow: LLM generates plan → user reviews → user approves/rejects.
/// </summary>
public class ActionPlan
{
    public string Summary { get; set; } = string.Empty;
    public List<PlannedAction> Actions { get; set; } = [];
    public bool RequiresApproval { get; set; } = true;
    public string? RiskLevel { get; set; }
    public int EstimatedElementsAffected { get; set; }
}

public class PlannedAction
{
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, object?> Arguments { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public string? ValidationRule { get; set; }
    public bool IsDestructive { get; set; }
}
