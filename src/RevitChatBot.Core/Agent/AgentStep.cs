namespace RevitChatBot.Core.Agent;

public enum AgentStepType
{
    Thought,
    Action,
    Observation,
    Answer,
    Confirmation
}

public class AgentStep
{
    public AgentStepType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? SkillName { get; set; }
    public Dictionary<string, object?>? Parameters { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class AgentPlan
{
    public string Goal { get; set; } = string.Empty;
    public List<AgentStep> Steps { get; set; } = [];
    public bool RequiresConfirmation { get; set; }
    public bool IsCompleted { get; set; }
    public string? FinalAnswer { get; set; }
    public string? ClarificationQuestion { get; set; }
    public List<string>? ClarificationOptions { get; set; }
    public string? ClarificationReason { get; set; }
    public bool NeedsClarification => ClarificationQuestion != null;
}
