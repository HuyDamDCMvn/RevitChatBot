using System.Text.Json;
using RevitChatBot.Core.Agent;
using RevitChatBot.Core.CodeGen;
using RevitChatBot.Core.LLM;
using RevitChatBot.Core.Memory;

namespace RevitChatBot.Core.Learning;

/// <summary>
/// Central meta-learning hub that cross-correlates signals from ALL learning modules.
/// Solves the key problem: individual learners (PlanReplayStore, ImprovementStore,
/// FewShotLearning, etc.) all operate in isolation. The Cortex connects them.
///
/// Three learning dimensions:
///   1. PROJECT: What's unique about this Revit model?
///   2. USER: What does this user care about and how do they work?
///   3. AGENT: What is the agent good/bad at and how can it improve?
///
/// The Cortex runs during idle time (between user messages) and produces a
/// consolidated "LearningInsight" that is injected into the agent's prompt.
///
/// Signal sources:
///   - InteractionRecorder → topic frequency, skill chain patterns
///   - PlanReplayStore → successful workflow templates
///   - ImprovementStore → recurring failure patterns
///   - SessionAnalytics → skill performance stats
///   - AdaptiveFewShotLearning → what worked before
///   - CodePatternLearning → API usage patterns
///   - SkillGapAnalyzer → what's missing
///   - VisualFeedbackLearner → visualization effectiveness
///   - KnowledgeSynthesizer → accumulated domain knowledge
/// </summary>
public class LearningCortex
{
    private readonly string _dataDir;
    private readonly ProjectProfiler _projectProfiler;
    private readonly UserBehaviorTracker _userTracker;
    private readonly CrossSkillCorrelator _correlator;
    private readonly ProactiveSuggestionEngine _suggestionEngine;

    private CortexSnapshot? _latestSnapshot;
    private DateTime _lastAnalysisUtc;
    private readonly TimeSpan _analysisInterval = TimeSpan.FromMinutes(5);

    public LearningCortex(string dataDir)
    {
        _dataDir = dataDir;
        Directory.CreateDirectory(dataDir);

        _projectProfiler = new ProjectProfiler(dataDir);
        _userTracker = new UserBehaviorTracker(dataDir);
        _correlator = new CrossSkillCorrelator();
        _suggestionEngine = new ProactiveSuggestionEngine();
    }

    public ProjectProfiler ProjectProfiler => _projectProfiler;
    public UserBehaviorTracker UserTracker => _userTracker;
    public CrossSkillCorrelator SkillCorrelator => _correlator;

    /// <summary>
    /// Feed a completed interaction to all sub-analyzers.
    /// Called by AgentOrchestrator after each plan completes.
    /// This is the main ingestion point — one event fans out to all learners.
    /// </summary>
    public void Ingest(AgentPlan plan, QueryAnalysis? analysis, SessionAnalytics? analytics)
    {
        if (!plan.IsCompleted) return;

        var skillsUsed = plan.Steps
            .Where(s => s.Type == AgentStepType.Action && s.SkillName is not null)
            .Select(s => s.SkillName!)
            .ToList();

        var observations = plan.Steps
            .Where(s => s.Type == AgentStepType.Observation)
            .Select(s => s.Content)
            .ToList();

        _userTracker.RecordInteraction(
            query: plan.Goal,
            intent: analysis?.Intent,
            category: analysis?.Category,
            skillsUsed: skillsUsed,
            stepCount: plan.Steps.Count,
            language: analysis?.Language);

        _correlator.RecordSkillSequence(
            skillsUsed, plan.IsCompleted, plan.Steps.Count);

        _projectProfiler.IngestFromObservations(observations, analysis?.Category);

        if (analytics is not null)
            _correlator.UpdatePerformanceStats(analytics);
    }

    /// <summary>
    /// Run a full cross-module analysis cycle. Call during idle time
    /// (e.g., 5 seconds after user's last message, before next message).
    /// Produces a CortexSnapshot with consolidated insights.
    /// </summary>
    public CortexSnapshot Analyze(
        InteractionRecorder? recorder = null,
        PlanReplayStore? planStore = null,
        ImprovementStore? improvementStore = null,
        SessionAnalytics? analytics = null,
        AdaptiveFewShotLearning? fewShot = null,
        CodePatternLearning? codePatterns = null)
    {
        if (DateTime.UtcNow - _lastAnalysisUtc < _analysisInterval && _latestSnapshot is not null)
            return _latestSnapshot;

        var snapshot = new CortexSnapshot
        {
            Timestamp = DateTime.UtcNow,
            ProjectProfile = _projectProfiler.GetProfile(),
            UserProfile = _userTracker.GetProfile(),
            SkillCorrelations = _correlator.GetTopCorrelations(10),
            ProactiveSuggestions = _suggestionEngine.Generate(
                _projectProfiler.GetProfile(),
                _userTracker.GetProfile(),
                _correlator.GetTopCorrelations(20),
                recorder?.GetRecentRecords(7) ?? [],
                improvementStore,
                analytics)
        };

        _latestSnapshot = snapshot;
        _lastAnalysisUtc = DateTime.UtcNow;
        return snapshot;
    }

    /// <summary>
    /// Generate a consolidated context block for the agent's system prompt.
    /// This is the key output — a single, prioritized summary of ALL learned knowledge
    /// that fits within a token budget.
    /// </summary>
    public string BuildCortexContext(int maxTokenBudget = 800)
    {
        var snapshot = _latestSnapshot;
        if (snapshot is null) return "";

        var sections = new List<(string Content, int Priority)>();

        // Priority 1: Proactive suggestions (highest value per token)
        if (snapshot.ProactiveSuggestions.Count > 0)
        {
            var suggestionsBlock = "--- PROACTIVE SUGGESTIONS (learned from past sessions) ---\n" +
                string.Join("\n", snapshot.ProactiveSuggestions
                    .Take(5).Select(s => $"  • {s.Suggestion} (confidence: {s.Confidence:F1})"));
            sections.Add((suggestionsBlock, 100));
        }

        // Priority 2: User preferences (adapts communication style)
        var userProfile = snapshot.UserProfile;
        if (userProfile.InteractionCount >= 3)
        {
            var userBlock = "--- USER PREFERENCES (learned) ---\n" +
                $"  Language: {userProfile.PreferredLanguage ?? "auto-detect"}\n" +
                $"  Top topics: {string.Join(", ", userProfile.TopIntents.Take(3).Select(kv => $"{kv.Key}({kv.Value}x)"))}\n" +
                $"  Expertise: {userProfile.EstimatedExpertise}\n" +
                (userProfile.PreferredDetailLevel is not null
                    ? $"  Detail preference: {userProfile.PreferredDetailLevel}\n" : "");
            sections.Add((userBlock, 90));
        }

        // Priority 3: Project-specific patterns
        var projectProfile = snapshot.ProjectProfile;
        if (projectProfile.KnownPatterns.Count > 0)
        {
            var projectBlock = "--- PROJECT PATTERNS (learned from this model) ---\n" +
                string.Join("\n", projectProfile.KnownPatterns
                    .Take(5).Select(p => $"  • {p}"));
            sections.Add((projectBlock, 80));
        }

        // Priority 4: Skill correlations (helps agent chain skills better)
        if (snapshot.SkillCorrelations.Count > 0)
        {
            var corrBlock = "--- SKILL PATTERNS (learned from usage) ---\n" +
                string.Join("\n", snapshot.SkillCorrelations
                    .Take(5).Select(c =>
                        $"  • {c.SkillA} + {c.SkillB}: co-occur {c.CoOccurrence}x" +
                        (c.OrderMatters ? $" (usually {c.SkillA} first)" : "")));
            sections.Add((corrBlock, 70));
        }

        // Assemble within token budget (rough: 1 token ≈ 4 chars)
        var result = new List<string>();
        int usedChars = 0;
        int charBudget = maxTokenBudget * 4;

        foreach (var (content, _) in sections.OrderByDescending(s => s.Priority))
        {
            if (usedChars + content.Length > charBudget) continue;
            result.Add(content);
            usedChars += content.Length;
        }

        return result.Count > 0 ? string.Join("\n\n", result) : "";
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await Task.WhenAll(
            _projectProfiler.SaveAsync(ct),
            _userTracker.SaveAsync(ct));
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await Task.WhenAll(
            _projectProfiler.LoadAsync(ct),
            _userTracker.LoadAsync(ct));
    }
}

public class CortexSnapshot
{
    public DateTime Timestamp { get; set; }
    public ProjectProfile ProjectProfile { get; set; } = new();
    public UserProfile UserProfile { get; set; } = new();
    public List<SkillCorrelation> SkillCorrelations { get; set; } = [];
    public List<ProactiveSuggestion> ProactiveSuggestions { get; set; } = [];
}
