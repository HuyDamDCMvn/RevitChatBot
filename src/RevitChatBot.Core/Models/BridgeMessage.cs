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
    public const string PartialIntent = "partial_intent";
    public const string HealthCheck = "health_check";
    public const string HealthStatus = "health_status";
    public const string ModelInfo = "model_info";
    public const string ModelInfoResponse = "model_info_response";
    public const string AutomationModeChanged = "automation_mode_changed";
    public const string ActionPlanReview = "action_plan_review";
    public const string ActionPlanApproval = "action_plan_approval";
    public const string WarningsDelta = "warnings_delta";
    public const string MemoryConsent = "memory_consent";
    public const string MemoryStats = "memory_stats";
    public const string VisionAnalysis = "vision_analysis";
    public const string ContextSnapshot = "context_snapshot";
    public const string ViewSnapshot = "view_snapshot";
    public const string ModelSync = "model_sync";
    public const string RequestSettings = "request_settings";
}
