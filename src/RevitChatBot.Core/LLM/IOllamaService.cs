using RevitChatBot.Core.Models;

namespace RevitChatBot.Core.LLM;

public class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen2.5:7b";
    public double Temperature { get; set; } = 0.3;
    public int? NumCtx { get; set; } = 8192;
    public string? KeepAlive { get; set; } = "10m";
    public bool? Think { get; set; }

    /// <summary>
    /// Optional cloud endpoint for heavy-reasoning tasks (e.g. "https://ollama.com").
    /// Requires CloudApiKey. When set, OllamaService can route complex tasks to cloud models.
    /// </summary>
    public string? CloudBaseUrl { get; set; }

    /// <summary>
    /// API key for Ollama Cloud. Required when using CloudBaseUrl.
    /// </summary>
    public string? CloudApiKey { get; set; }

    /// <summary>
    /// Cloud model name for heavy tasks (e.g. "gpt-oss:120b-cloud", "deepseek-r1:70b").
    /// </summary>
    public string? CloudModel { get; set; }

    /// <summary>
    /// Enable logprobs in /api/chat responses for confidence scoring.
    /// </summary>
    public bool? Logprobs { get; set; }

    /// <summary>
    /// Number of top log probabilities per token (requires Logprobs=true).
    /// </summary>
    public int? TopLogprobs { get; set; }
}

public interface IOllamaService
{
    Task<ChatMessage> ChatAsync(
        List<ChatMessage> messages,
        List<ToolDefinition>? tools = null,
        string? formatJson = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> ChatStreamAsync(
        List<ChatMessage> messages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Call /api/generate with structured output (format parameter).
    /// Returns raw response text. Useful for intent extraction, structured analysis.
    /// </summary>
    Task<string> GenerateAsync(
        string prompt,
        string? formatJson = null,
        double? temperature = null,
        int? numCtx = null,
        CancellationToken cancellationToken = default);

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task<List<OllamaModel>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get detailed info about a model: context length, families, parameters, template.
    /// </summary>
    Task<ModelInfo?> ShowModelAsync(string? modelName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// List models currently loaded into memory with VRAM usage and expiry info.
    /// </summary>
    Task<List<RunningModel>> ListRunningModelsAsync(CancellationToken cancellationToken = default);

    void UpdateOptions(Action<OllamaOptions> configure);
    OllamaOptions GetCurrentOptions();
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

public class ModelInfo
{
    public string ModelName { get; set; } = string.Empty;
    public int ContextLength { get; set; }
    public string Family { get; set; } = string.Empty;
    public string ParameterSize { get; set; } = string.Empty;
    public string QuantizationLevel { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public string? System { get; set; }
}

public class RunningModel
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public long SizeVram { get; set; }
    public string ParameterSize { get; set; } = string.Empty;
    public string QuantizationLevel { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Confidence information from logprobs when selecting tools.
/// </summary>
public class ToolSelectionConfidence
{
    public string ToolName { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public bool IsLowConfidence => Confidence < 0.3;
}
