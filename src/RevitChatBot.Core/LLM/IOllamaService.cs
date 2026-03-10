using RevitChatBot.Core.Models;

namespace RevitChatBot.Core.LLM;

public class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen2.5:7b";
    public double Temperature { get; set; } = 0.7;
    public int? NumCtx { get; set; } = 4096;
    public string? KeepAlive { get; set; } = "5m";
    public bool? Think { get; set; }
}

public interface IOllamaService
{
    Task<ChatMessage> ChatAsync(
        List<ChatMessage> messages,
        List<ToolDefinition>? tools = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> ChatStreamAsync(
        List<ChatMessage> messages,
        CancellationToken cancellationToken = default);

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task<List<OllamaModel>> ListModelsAsync(CancellationToken cancellationToken = default);
    void UpdateOptions(Action<OllamaOptions> configure);
}

public class OllamaModel
{
    public string Name { get; set; } = string.Empty;
    public string ParameterSize { get; set; } = string.Empty;
    public string QuantizationLevel { get; set; } = string.Empty;
    public long Size { get; set; }
}

public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, ToolParameter> Parameters { get; set; } = new();
    public List<string> Required { get; set; } = new();
}

public class ToolParameter
{
    public string Type { get; set; } = "string";
    public string Description { get; set; } = string.Empty;
    public List<string>? Enum { get; set; }
}
