using RevitChatBot.Core.Models;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// Routes LLM requests between local Ollama, local CodeGen model, and Ollama Cloud
/// based on task complexity. Priority: CodeGen model (local, free) → Cloud → Local fallback.
/// </summary>
public class OllamaCloudRouter
{
    private readonly OllamaService _localService;
    private OllamaService? _cloudService;
    private OllamaService? _codeGenService;
    private OllamaOptions _options;

    private static readonly HashSet<string> HeavyReasoningSkills =
    [
        "execute_revit_code",
        "advanced_clash_detection",
        "avoid_clash",
        "compliance_check",
        "model_audit"
    ];

    public bool IsCloudAvailable => _cloudService != null;
    public bool IsCodeGenModelAvailable => _codeGenService != null;
    public string ActiveEndpoint { get; private set; } = "local";

    public OllamaCloudRouter(OllamaService localService)
    {
        _localService = localService;
        _options = localService.GetCurrentOptions();
        TryInitializeCloud();
        TryInitializeCodeGen();
    }

    /// <summary>
    /// Initialize or reinitialize cloud client based on current options.
    /// </summary>
    public void TryInitializeCloud()
    {
        _options = _localService.GetCurrentOptions();

        if (string.IsNullOrWhiteSpace(_options.CloudBaseUrl) ||
            string.IsNullOrWhiteSpace(_options.CloudApiKey))
        {
            _cloudService = null;
            return;
        }

        _cloudService = new OllamaService(new OllamaOptions
        {
            BaseUrl = _options.CloudBaseUrl,
            Model = _options.CloudModel ?? "gpt-oss:120b-cloud",
            Temperature = _options.Temperature,
            NumCtx = null,
            KeepAlive = null,
            Think = _options.Think
        });
    }

    /// <summary>
    /// Initialize or reinitialize the dedicated local CodeGen model client.
    /// Uses the same Ollama server but with a code-specialized model and lower temperature.
    /// </summary>
    public void TryInitializeCodeGen()
    {
        _options = _localService.GetCurrentOptions();

        if (string.IsNullOrWhiteSpace(_options.CodeGenModel))
        {
            _codeGenService?.Dispose();
            _codeGenService = null;
            return;
        }

        if (_codeGenService != null)
        {
            _codeGenService.Dispose();
            _codeGenService = null;
        }

        _codeGenService = new OllamaService(new OllamaOptions
        {
            BaseUrl = _options.BaseUrl,
            Model = _options.CodeGenModel,
            Temperature = 0.1,
            NumCtx = _options.NumCtx,
            KeepAlive = "5m",
            Think = _options.Think
        });
    }

    /// <summary>
    /// Route a chat request to the appropriate endpoint based on complexity signals.
    /// Priority: CodeGen model (for code tasks) → Cloud (for heavy reasoning) → Local.
    /// </summary>
    public async Task<ChatMessage> ChatWithRoutingAsync(
        List<ChatMessage> messages,
        List<ToolDefinition>? tools,
        ComplexityHint hint,
        CancellationToken ct = default)
    {
        if (_codeGenService != null && hint.IsCodeGenTask)
        {
            try
            {
                ActiveEndpoint = $"local-codegen ({_options.CodeGenModel})";
                return await _codeGenService.ChatAsync(messages, tools, cancellationToken: ct);
            }
            catch
            {
                ActiveEndpoint = "local (codegen fallback)";
            }
        }

        if (_cloudService != null && ShouldUseCloud(hint))
        {
            try
            {
                ActiveEndpoint = "cloud";
                return await _cloudService.ChatAsync(messages, tools, cancellationToken: ct);
            }
            catch
            {
                ActiveEndpoint = "local (cloud fallback)";
            }
        }

        ActiveEndpoint = "local";
        return await _localService.ChatAsync(messages, tools, cancellationToken: ct);
    }

    private bool ShouldUseCloud(ComplexityHint hint)
    {
        if (hint.ForceLocal) return false;
        if (hint.ForceCloud) return true;

        if (hint.IsCodeGenRetry) return true;
        if (hint.ReActStep >= 5) return true;
        if (hint.ActiveSkills?.Any(s => HeavyReasoningSkills.Contains(s)) == true) return true;
        if (hint.EstimatedComplexity >= ComplexityLevel.High) return true;

        return false;
    }
}

public enum ComplexityLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public class ComplexityHint
{
    public ComplexityLevel EstimatedComplexity { get; set; } = ComplexityLevel.Low;
    public bool IsCodeGenRetry { get; set; }
    public bool IsCodeGenTask { get; set; }
    public int ReActStep { get; set; }
    public List<string>? ActiveSkills { get; set; }
    public bool ForceLocal { get; set; }
    public bool ForceCloud { get; set; }
}
