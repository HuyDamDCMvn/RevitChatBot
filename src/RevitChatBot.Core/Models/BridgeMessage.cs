namespace RevitChatBot.Core.Models;

public class BridgeMessage
{
    public string Type { get; set; } = string.Empty;
    public string? Content { get; set; }
    public Dictionary<string, object?>? Data { get; set; }
}

public static class BridgeMessageTypes
{
    public const string UserMessage = "user_message";
    public const string AssistantMessage = "assistant_message";
    public const string SkillExecuting = "skill_executing";
    public const string SkillCompleted = "skill_completed";
    public const string Error = "error";
    public const string StreamChunk = "stream_chunk";
    public const string StreamEnd = "stream_end";
    public const string SettingsUpdate = "settings_update";
    public const string AgentThinking = "agent_thinking";
    public const string AgentStep = "agent_step";
    public const string ConfirmationRequired = "confirmation_required";
    public const string ConfirmationResponse = "confirmation_response";
    public const string ClarificationRequest = "clarification_request";
    public const string ClarificationResponse = "clarification_response";
}
