using RevitChatBot.Core.CodeGen;
using RevitChatBot.Core.Context;
using RevitChatBot.Core.LLM;
using RevitChatBot.Core.Memory;
using RevitChatBot.Core.Models;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.Core.Agent;

/// <summary>
/// Enhanced chat session with cross-session memory, conversation summarization,
/// learned facts, user preferences, and self-evolving codegen skills.
/// Wraps AgentOrchestrator for backward compatibility.
/// </summary>
public class ChatSessionV2
{
    private readonly AgentOrchestrator _agent;
    private readonly IOllamaService _ollama;
    private readonly PromptBuilder _promptBuilder;
    private readonly ContextManager _contextManager;
    private readonly MemoryManager? _memory;

    private bool _useAgentMode = true;
    private int _messagesSinceLastPersist;
    private const int PersistEveryNMessages = 5;

    public IReadOnlyList<ChatMessage> History => _agent.History;

    public event Action<AgentStep>? OnAgentStep;
    public event Action<string>? OnStreamChunk;
    public event Action<string>? OnThinking;
    public event Func<string, Task<bool>>? OnConfirmationRequired;

    public ChatSessionV2(
        IOllamaService ollama,
        SkillRegistry skillRegistry,
        SkillExecutor skillExecutor,
        ContextManager contextManager,
        PromptBuilder? promptBuilder = null,
        MemoryManager? memory = null,
        CodeGenLibrary? codeGenLibrary = null,
        DynamicSkillRegistry? dynamicSkillRegistry = null,
        CodePatternLearning? patternLearning = null)
    {
        _ollama = ollama;
        _contextManager = contextManager;
        _promptBuilder = promptBuilder ?? new PromptBuilder();
        _memory = memory;

        _agent = new AgentOrchestrator(
            ollama, skillRegistry, skillExecutor, contextManager, _promptBuilder, memory,
            codeGenLibrary, dynamicSkillRegistry, patternLearning);
        _agent.OnStepExecuted += step => OnAgentStep?.Invoke(step);
        _agent.OnThinking += thought => OnThinking?.Invoke(thought);
    }

    /// <summary>
    /// Initialize memory and restore previous conversation if available.
    /// Call this after construction with the project key.
    /// </summary>
    public async Task InitializeMemoryAsync(string projectKey, CancellationToken ct = default)
    {
        if (_memory == null) return;

        await _memory.InitializeAsync(projectKey, ct);

        var restored = await _memory.GetRestoredHistoryAsync(ct);
        if (restored.Count > 0)
        {
            foreach (var msg in restored)
                _agent.MutableHistory.Add(msg);
        }
    }

    public void SetAgentMode(bool enabled) => _useAgentMode = enabled;

    public async Task<string> SendMessageAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        string result;

        if (_useAgentMode)
        {
            if (OnConfirmationRequired is not null)
                _agent.OnConfirmationRequired += OnConfirmationRequired;

            try
            {
                var plan = await _agent.ExecuteAsync(userMessage, cancellationToken);
                result = plan.FinalAnswer ?? "No response generated.";
            }
            finally
            {
                if (OnConfirmationRequired is not null)
                    _agent.OnConfirmationRequired -= OnConfirmationRequired;
            }
        }
        else
        {
            result = await SimpleChatAsync(userMessage, cancellationToken);
        }

        _messagesSinceLastPersist++;
        if (_memory != null && _messagesSinceLastPersist >= PersistEveryNMessages)
        {
            _messagesSinceLastPersist = 0;
            _ = PersistMemoryAsync(cancellationToken);
        }

        return result;
    }

    private async Task<string> SimpleChatAsync(string userMessage, CancellationToken ct)
    {
        var context = await _contextManager.GatherContextAsync();
        var messages = _promptBuilder.Build(
            [.. _agent.History, ChatMessage.FromUser(userMessage)], context);

        var fullContent = new System.Text.StringBuilder();
        await foreach (var chunk in _ollama.ChatStreamAsync(messages, ct))
        {
            fullContent.Append(chunk);
            OnStreamChunk?.Invoke(chunk);
        }

        return fullContent.ToString();
    }

    /// <summary>
    /// Persist all memory to disk. Call on window close or periodically.
    /// </summary>
    public async Task PersistMemoryAsync(CancellationToken ct = default)
    {
        if (_memory == null) return;
        try
        {
            await _memory.PersistAsync([.. _agent.History], ct);
        }
        catch
        {
            // Non-critical — don't crash on persist failure
        }
    }

    public void ClearHistory()
    {
        _agent.ClearHistory();
        _memory?.ClearProjectMemory();
    }
}
