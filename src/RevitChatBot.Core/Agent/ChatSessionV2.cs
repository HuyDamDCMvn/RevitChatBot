using RevitChatBot.Core.CodeGen;
using RevitChatBot.Core.Context;
using RevitChatBot.Core.Learning;
using RevitChatBot.Core.LLM;
using RevitChatBot.Core.Memory;
using RevitChatBot.Core.Models;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.Core.Agent;

/// <summary>
/// Enhanced chat session with cross-session memory, conversation summarization,
/// learned facts, user preferences, self-evolving codegen skills, and
/// self-training pipeline (plan replay, self-evaluation, composite skills).
/// </summary>
public class ChatSessionV2
{
    private readonly AgentOrchestrator _agent;
    private readonly IOllamaService _ollama;
    private readonly PromptBuilder _promptBuilder;
    private readonly ContextManager _contextManager;
    private readonly MemoryManager? _memory;
    private readonly SelfLearningPersistenceManager? _persistenceManager;
    private readonly SelfTrainingScheduler? _selfTrainingScheduler;

    private bool _useAgentMode = true;
    private int _messagesSinceLastPersist;
    private const int PersistEveryNMessages = 5;

    public IReadOnlyList<ChatMessage> History => _agent.History;
    public AgentPlan? LastPlan { get; private set; }

    public event Action<AgentStep>? OnAgentStep;
    public event Action<string>? OnStreamChunk;
    public event Action<string>? OnThinking;
    public event Func<string, Task<bool>>? OnConfirmationRequired;
    public event Func<ActionPlan, Task<bool>>? OnActionPlanReview;
    public event Func<Task<WarningsCaptureResult>>? OnCaptureWarnings;

    public ChatSessionV2(
        IOllamaService ollama,
        SkillRegistry skillRegistry,
        SkillExecutor skillExecutor,
        ContextManager contextManager,
        PromptBuilder? promptBuilder = null,
        MemoryManager? memory = null,
        CodeGenLibrary? codeGenLibrary = null,
        DynamicSkillRegistry? dynamicSkillRegistry = null,
        CodePatternLearning? patternLearning = null,
        QueryPreprocessor? queryPreprocessor = null,
        AdaptivePromptBuilder? adaptivePromptBuilder = null,
        SemanticSkillRouter? skillRouter = null,
        ConversationQueryRewriter? queryRewriter = null,
        ContextWindowOptimizer? contextOptimizer = null,
        MultiIntentDecomposer? intentDecomposer = null,
        AdaptiveFewShotLearning? fewShotLearning = null,
        DynamicGlossary? dynamicGlossary = null,
        SkillSuccessFeedback? skillFeedback = null,
        PromptCache? promptCache = null,
        AgentLogger? agentLogger = null,
        PlanReplayStore? planReplayStore = null,
        InteractionRecorder? interactionRecorder = null,
        SelfEvaluator? selfEvaluator = null,
        ImprovementStore? improvementStore = null,
        CompositeSkillEngine? compositeEngine = null,
        SelfLearningPersistenceManager? persistenceManager = null,
        SelfTrainingScheduler? selfTrainingScheduler = null,
        LearningCortex? learningCortex = null,
        FailureRecoveryLearner? failureRecovery = null,
        KnowledgeCodeTemplateStore? codeTemplateStore = null,
        SkillKnowledgeIndex? skillKnowledgeIndex = null,
        DynamicCodeExamplesLibrary? dynamicExamplesLibrary = null,
        CodeGenKnowledgeEnricher? codeGenKnowledgeEnricher = null,
        SkillKnowledgeRecipeStore? recipeStore = null,
        ContextUsageTracker? contextUsageTracker = null)
    {
        _ollama = ollama;
        _contextManager = contextManager;
        _promptBuilder = promptBuilder ?? new PromptBuilder();
        _memory = memory;
        _persistenceManager = persistenceManager;
        _selfTrainingScheduler = selfTrainingScheduler;

        _agent = new AgentOrchestrator(
            ollama, skillRegistry, skillExecutor, contextManager, _promptBuilder, memory,
            codeGenLibrary, dynamicSkillRegistry, patternLearning,
            queryPreprocessor, adaptivePromptBuilder, skillRouter,
            queryRewriter, contextOptimizer, intentDecomposer,
            fewShotLearning, dynamicGlossary, skillFeedback, promptCache,
            agentLogger,
            planReplayStore, interactionRecorder, selfEvaluator,
            improvementStore, compositeEngine, persistenceManager,
            learningCortex, failureRecovery,
            codeTemplateStore, skillKnowledgeIndex, dynamicExamplesLibrary,
            codeGenKnowledgeEnricher, recipeStore,
            contextUsageTracker);
        _agent.OnStepExecuted += step => OnAgentStep?.Invoke(step);
        _agent.OnThinking += thought => OnThinking?.Invoke(thought);
    }

    /// <summary>
    /// Access to the underlying orchestrator for advanced wiring (e.g., VisualFeedbackTracker).
    /// </summary>
    public AgentOrchestrator Orchestrator => _agent;

    /// <summary>
    /// Initialize memory and restore previous conversation if available.
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

    /// <summary>
    /// Perform streaming intent detection on partial input (while typing).
    /// Returns predicted intent/category or null if not enough input.
    /// </summary>
    public static PartialAnalysis? AnalyzePartialInput(string partialInput) =>
        StreamingIntentDetector.AnalyzePartial(partialInput);

    public async Task<string> SendMessageAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        string result;

        if (_useAgentMode)
        {
            if (OnConfirmationRequired is not null)
                _agent.OnConfirmationRequired += OnConfirmationRequired;
            if (OnActionPlanReview is not null)
                _agent.OnActionPlanReview += OnActionPlanReview;
            if (OnCaptureWarnings is not null)
                _agent.OnCaptureWarnings += OnCaptureWarnings;

            try
            {
                var plan = await _agent.ExecuteAsync(userMessage, cancellationToken);
                LastPlan = plan;
                result = plan.FinalAnswer ?? "No response generated.";
            }
            finally
            {
                if (OnConfirmationRequired is not null)
                    _agent.OnConfirmationRequired -= OnConfirmationRequired;
                if (OnActionPlanReview is not null)
                    _agent.OnActionPlanReview -= OnActionPlanReview;
                if (OnCaptureWarnings is not null)
                    _agent.OnCaptureWarnings -= OnCaptureWarnings;
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
    /// Persist all memory and self-learning data to disk.
    /// </summary>
    public async Task PersistMemoryAsync(CancellationToken ct = default)
    {
        var tasks = new List<Task>();

        if (_memory != null)
            tasks.Add(SafeAsync(() => _memory.PersistAsync([.. _agent.History], ct)));

        if (_persistenceManager != null)
            tasks.Add(SafeAsync(() => _persistenceManager.PersistAllAsync(ct)));

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Start the background self-training scheduler.
    /// </summary>
    public void StartSelfTraining(TimeSpan? interval = null)
    {
        _selfTrainingScheduler?.Start(interval ?? TimeSpan.FromMinutes(30));
    }

    /// <summary>
    /// Stop the background self-training scheduler.
    /// </summary>
    public void StopSelfTraining()
    {
        _selfTrainingScheduler?.Stop();
    }

    /// <summary>
    /// Manually trigger a single self-training cycle.
    /// </summary>
    public async Task RunSelfTrainingCycleAsync(CancellationToken ct = default)
    {
        if (_selfTrainingScheduler != null)
            await _selfTrainingScheduler.RunCycleAsync(ct);
    }

    public void ClearHistory()
    {
        _agent.ClearHistory();
        _memory?.ClearProjectMemory();
    }

    private static async Task SafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch { /* non-critical */ }
    }
}
