namespace RevitChatBot.Core.Models;

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool
}

public class ChatMessage
{
    public ChatRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<ToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// For tool role messages: the name of the tool that produced this result.
    /// Ollama API uses "tool_name" on tool response messages.
    /// </summary>
    public string? ToolName { get; set; }

    /// <summary>
    /// For thinking models (e.g. QwQ, DeepSeek-R1): the model's internal reasoning
    /// process returned in the "thinking" field when think=true.
    /// </summary>
    public string? Thinking { get; set; }

    public static ChatMessage FromSystem(string content) =>
        new() { Role = ChatRole.System, Content = content };

    public static ChatMessage FromUser(string content) =>
        new() { Role = ChatRole.User, Content = content };

    public static ChatMessage FromAssistant(string content) =>
        new() { Role = ChatRole.Assistant, Content = content };

    public static ChatMessage FromTool(string toolName, string content) =>
        new() { Role = ChatRole.Tool, Content = content, ToolName = toolName };
}

public class ToolCall
{
    public string FunctionName { get; set; } = string.Empty;
    public Dictionary<string, object?> Arguments { get; set; } = new();
}
