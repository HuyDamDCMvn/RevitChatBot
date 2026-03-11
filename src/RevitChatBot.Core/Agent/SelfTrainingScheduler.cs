using RevitChatBot.Core.CodeGen;
using RevitChatBot.Core.LLM;

namespace RevitChatBot.Core.Agent;

/// <summary>
/// Background scheduler that runs self-training tasks during idle time:
/// - Composite skill discovery (from repeated plan patterns)
/// - Knowledge synthesis (from accumulated interactions)
/// - Skill gap analysis (weekly)
/// - Workflow discovery (using LLM)
/// - Recipe discovery (skill+knowledge combos)
/// - Persistence of all learning data
/// </summary>
public class SelfTrainingScheduler : IDisposable
{
    private Timer? _timer;
    private readonly CompositeSkillEngine? _compositeEngine;
    private readonly InteractionRecorder? _recorder;
    private readonly SkillGapAnalyzer? _gapAnalyzer;
    private readonly SkillDiscoveryAgent? _discoveryAgent;
    private readonly SelfLearningPersistenceManager? _persistenceManager;
    private readonly SkillKnowledgeRecipeStore? _recipeStore;
    private readonly AgentLogger? _logger;

    private readonly SemaphoreSlim _runLock = new(1, 1);
    private DateTime _lastGapAnalysis = DateTime.MinValue;
    private DateTime _lastWorkflowDiscovery = DateTime.MinValue;

    /// <summary>
    /// Delegate for knowledge synthesis (injected to avoid circular dependency with Knowledge layer).
    /// </summary>
    public Func<List<InteractionRecord>, CancellationToken, Task<int>>? OnSynthesizeKnowledge { get; set; }

    /// <summary>
    /// Delegate to set prioritized topics on KnowledgeSynthesizer before synthesis runs.
    /// Wired from WebViewBridge to feed recipe frequency data into knowledge synthesis.
    /// </summary>
    public Action<List<string>>? OnSetSynthesisPriorities { get; set; }

    public SelfTrainingScheduler(
        CompositeSkillEngine? compositeEngine = null,
        InteractionRecorder? recorder = null,
        SkillGapAnalyzer? gapAnalyzer = null,
        SkillDiscoveryAgent? discoveryAgent = null,
        SelfLearningPersistenceManager? persistenceManager = null,
        AgentLogger? logger = null,
        SkillKnowledgeRecipeStore? recipeStore = null)
    {
        _compositeEngine = compositeEngine;
        _recorder = recorder;
        _gapAnalyzer = gapAnalyzer;
        _discoveryAgent = discoveryAgent;
        _persistenceManager = persistenceManager;
        _logger = logger;
        _recipeStore = recipeStore;
    }

    /// <summary>
    /// Start the background training loop.
    /// </summary>
    public void Start(TimeSpan interval)
    {
        _timer = new Timer(_ => _ = RunCycleAsync(), null, interval, interval);
    }

    /// <summary>
    /// Run a single self-training cycle. Can also be invoked manually.
    /// </summary>
    public async Task RunCycleAsync(CancellationToken ct = default)
    {
        if (!await _runLock.WaitAsync(0, ct)) return;

        try
        {
            await RunCompositeDiscovery(ct);
            await RunKnowledgeSynthesis(ct);
            await RunGapAnalysis(ct);
            await RunWorkflowDiscovery(ct);
            RunRecipeAnalysis();

            if (_persistenceManager != null)
                await _persistenceManager.PersistAllAsync(ct);
        }
        catch { /* background task — never crash */ }
        finally
        {
            _runLock.Release();
        }
    }

    private Task RunCompositeDiscovery(CancellationToken ct)
    {
        if (_compositeEngine == null) return Task.CompletedTask;

        try
        {
            var candidates = _compositeEngine.DiscoverCandidates(minUseCount: 3);
            int promoted = 0;
            foreach (var c in candidates.Take(3))
            {
                if (_compositeEngine.PromoteToCompositeSkill(c))
                    promoted++;
            }

            if (promoted > 0)
                _logger?.LogToolExecution("self_training_composite_discovery",
                    new Dictionary<string, object?> { ["promoted_count"] = promoted },
                    true, 0);
        }
        catch { /* non-critical */ }

        return Task.CompletedTask;
    }

    private async Task RunKnowledgeSynthesis(CancellationToken ct)
    {
        if (_recorder == null || OnSynthesizeKnowledge == null) return;
        if (!_recorder.ShouldSynthesize) return;

        try
        {
            if (_recipeStore != null && OnSetSynthesisPriorities != null)
            {
                var frequentRecipes = _recipeStore.DiscoverFrequentRecipes(minUseCount: 2);
                var topics = frequentRecipes
                    .Select(r => r.Intent)
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Distinct()
                    .ToList();
                OnSetSynthesisPriorities(topics);
            }

            var records = _recorder.GetRecentRecords(days: 7);
            var articlesCreated = await OnSynthesizeKnowledge(records, ct);

            if (articlesCreated > 0)
                _logger?.LogToolExecution("self_training_knowledge_synthesis",
                    new Dictionary<string, object?> { ["articles_created"] = articlesCreated },
                    true, 0);
        }
        catch { /* non-critical */ }
    }

    private async Task RunGapAnalysis(CancellationToken ct)
    {
        if (_gapAnalyzer == null) return;
        if ((DateTime.UtcNow - _lastGapAnalysis).TotalDays < 7) return;

        try
        {
            _lastGapAnalysis = DateTime.UtcNow;
            var gaps = await _gapAnalyzer.AnalyzeGaps(ct: ct);

            if (gaps.Count > 0)
            {
                var summary = _gapAnalyzer.GetGapSummary(gaps);
                _logger?.LogToolExecution("self_training_gap_analysis",
                    new Dictionary<string, object?>
                    {
                        ["gap_count"] = gaps.Count,
                        ["high_priority"] = gaps.Count(g => g.Priority == "high")
                    },
                    true, 0);
            }
        }
        catch { /* non-critical */ }
    }

    private async Task RunWorkflowDiscovery(CancellationToken ct)
    {
        if (_discoveryAgent == null) return;
        if ((DateTime.UtcNow - _lastWorkflowDiscovery).TotalDays < 3) return;

        try
        {
            _lastWorkflowDiscovery = DateTime.UtcNow;
            var workflows = await _discoveryAgent.DiscoverWorkflows(ct);

            int promoted = 0;
            if (_compositeEngine != null)
            {
                foreach (var wf in workflows.Take(3))
                {
                    if (_compositeEngine.PromoteFromWorkflow(wf))
                        promoted++;
                }
            }

            if (workflows.Count > 0)
                _logger?.LogToolExecution("self_training_workflow_discovery",
                    new Dictionary<string, object?>
                    {
                        ["workflows_found"] = workflows.Count,
                        ["promoted_to_composite"] = promoted
                    },
                    true, 0);
        }
        catch { /* non-critical */ }
    }

    private void RunRecipeAnalysis()
    {
        if (_recipeStore == null) return;

        try
        {
            var frequentRecipes = _recipeStore.DiscoverFrequentRecipes(minUseCount: 3);
            if (frequentRecipes.Count > 0)
                _logger?.LogToolExecution("self_training_recipe_analysis",
                    new Dictionary<string, object?> { ["frequent_recipes"] = frequentRecipes.Count },
                    true, 0);
        }
        catch { /* non-critical */ }
    }

    public void Stop() => _timer?.Change(Timeout.Infinite, Timeout.Infinite);

    public void Dispose()
    {
        _timer?.Dispose();
        _runLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
