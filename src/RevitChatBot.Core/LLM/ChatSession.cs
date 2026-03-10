using RevitChatBot.Core.Context;
using RevitChatBot.Core.Models;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.Core.LLM;

public class ChatSession
{
    private readonly IOllamaService _ollama;
    private readonly SkillRegistry _skillRegistry;
    private readonly SkillExecutor _skillExecutor;
    private readonly ContextManager _contextManager;
    private readonly PromptBuilder _promptBuilder;
    private readonly List<ChatMessage> _history = new();

    private const int MaxToolRounds = 5;

    public IReadOnlyList<ChatMessage> History => _history.AsReadOnly();

    public event Action<string>? OnSkillExecuting;
    public event Action<string, SkillResult>? OnSkillCompleted;
    public event Action<string>? OnStreamChunk;

    public ChatSession(
        IOllamaService ollama,
        SkillRegistry skillRegistry,
        SkillExecutor skillExecutor,
        ContextManager contextManager,
        PromptBuilder? promptBuilder = null)
    {
        _ollama = ollama;
        _skillRegistry = skillRegistry;
        _skillExecutor = skillExecutor;
        _contextManager = contextManager;
        _promptBuilder = promptBuilder ?? new PromptBuilder();
    }

    public async Task<string> SendMessageAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        _history.Add(ChatMessage.FromUser(userMessage));

        var context = await _contextManager.GatherContextAsync();
        var toolDefs = _promptBuilder.BuildToolDefinitions(_skillRegistry.GetAllDescriptors());
        var messages = _promptBuilder.Build(_history, context);

        var round = 0;
        while (round < MaxToolRounds)
        {
            var response = await _ollama.ChatAsync(messages, toolDefs, cancellationToken);

            if (response.ToolCalls is not { Count: > 0 })
            {
                _history.Add(response);
                return response.Content;
            }

            messages.Add(response);
            _history.Add(response);

            foreach (var toolCall in response.ToolCalls)
            {
                OnSkillExecuting?.Invoke(toolCall.FunctionName);

                var result = await _skillExecutor.ExecuteAsync(
                    toolCall.FunctionName, toolCall.Arguments, cancellationToken);

                OnSkillCompleted?.Invoke(toolCall.FunctionName, result);

                var toolMessage = ChatMessage.FromTool(toolCall.FunctionName, result.ToJson());
                messages.Add(toolMessage);
                _history.Add(toolMessage);
            }

            round++;
        }

        var finalResponse = await _ollama.ChatAsync(messages, cancellationToken: cancellationToken);
        _history.Add(finalResponse);
        return finalResponse.Content;
    }

    public async Task StreamMessageAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        _history.Add(ChatMessage.FromUser(userMessage));

        var context = await _contextManager.GatherContextAsync();
        var messages = _promptBuilder.Build(_history, context);

        var fullContent = new System.Text.StringBuilder();
        await foreach (var chunk in _ollama.ChatStreamAsync(messages, cancellationToken))
        {
            fullContent.Append(chunk);
            OnStreamChunk?.Invoke(chunk);
        }

        _history.Add(ChatMessage.FromAssistant(fullContent.ToString()));
    }

    public void ClearHistory() => _history.Clear();
}
