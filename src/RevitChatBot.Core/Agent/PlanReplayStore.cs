using System.Text.Json;
using RevitChatBot.Core.LLM;

namespace RevitChatBot.Core.Agent;

/// <summary>
/// Stores successful multi-step plans for replay.
/// When a similar query comes in, the stored plan is injected as a hint
/// so the LLM can replicate or improve upon the previous approach.
/// Uses semantic search (embedding) with keyword fallback.
/// </summary>
public class PlanReplayStore
{
    private readonly string _filePath;
    private readonly ISkillEmbeddingProvider? _embeddingProvider;
    private List<StoredPlan> _plans = [];
    private bool _loaded;

    private const int MaxPlans = 200;
    private const double SemanticMatchThreshold = 0.70;

    public PlanReplayStore(string filePath, ISkillEmbeddingProvider? embeddingProvider = null)
    {
        _filePath = filePath;
        _embeddingProvider = embeddingProvider;
    }

    public IReadOnlyList<StoredPlan> Plans => _plans.AsReadOnly();

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_loaded) return;
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, ct);
                _plans = JsonSerializer.Deserialize<List<StoredPlan>>(json, JsonOpts) ?? [];
            }
        }
        catch { _plans = []; }
        _loaded = true;
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_plans, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch { /* non-critical */ }
    }

    /// <summary>
    /// Record a successful multi-step plan for future replay.
    /// </summary>
    public async Task RecordSuccessfulPlan(
        AgentPlan plan, QueryAnalysis? analysis, CancellationToken ct = default)
    {
        if (!plan.IsCompleted || plan.FinalAnswer == null) return;

        var skillChain = plan.Steps
            .Where(s => s.Type == AgentStepType.Action && s.SkillName != null)
            .Select(s => new StoredSkillCall
            {
                SkillName = s.SkillName!,
                Parameters = s.Parameters ?? new(),
                ObservationSummary = GetObservationAfter(plan.Steps, s)
            })
            .ToList();

        if (skillChain.Count == 0) return;

        var chainKey = string.Join("→", skillChain.Select(s => s.SkillName));
        var existing = _plans.FirstOrDefault(p =>
            p.Intent == analysis?.Intent &&
            p.Category == analysis?.Category &&
            string.Join("→", p.SkillChain.Select(s => s.SkillName)) == chainKey);

        if (existing != null)
        {
            existing.UseCount++;
            existing.LastUsed = DateTime.UtcNow;
            return;
        }

        float[]? embedding = null;
        if (_embeddingProvider != null)
        {
            try { embedding = await _embeddingProvider.GetEmbeddingAsync(plan.Goal, ct); }
            catch { /* non-critical */ }
        }

        _plans.Add(new StoredPlan
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Goal = plan.Goal,
            Intent = analysis?.Intent,
            Category = analysis?.Category,
            SkillChain = skillChain,
            FinalAnswerSummary = Truncate(plan.FinalAnswer, 300),
            GoalEmbedding = embedding,
            CreatedAt = DateTime.UtcNow,
            LastUsed = DateTime.UtcNow,
            UseCount = 1
        });

        PruneOldPlans();
    }

    /// <summary>
    /// Find a previously successful plan that matches the current query.
    /// </summary>
    public async Task<StoredPlan?> FindSimilarPlan(
        string query, QueryAnalysis? analysis, CancellationToken ct = default)
    {
        if (_plans.Count == 0) return null;

        if (_embeddingProvider != null)
        {
            try
            {
                var queryEmb = await _embeddingProvider.GetEmbeddingAsync(query, ct);
                var best = _plans
                    .Where(p => p.GoalEmbedding != null)
                    .Select(p => (plan: p, score: CosineSimilarity(queryEmb, p.GoalEmbedding!)))
                    .Where(x => x.score > SemanticMatchThreshold)
                    .OrderByDescending(x => x.score)
                    .FirstOrDefault();

                if (best.plan != null)
                {
                    best.plan.UseCount++;
                    return best.plan;
                }
            }
            catch { /* fall through to keyword matching */ }
        }

        return KeywordMatch(query, analysis);
    }

    /// <summary>
    /// Get all plan chain patterns for composite skill discovery.
    /// </summary>
    public List<StoredPlan> GetAllPlans() => [.. _plans];

    private StoredPlan? KeywordMatch(string query, QueryAnalysis? analysis)
    {
        if (analysis == null) return null;

        var queryLower = query.ToLowerInvariant();
        var queryWords = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2).ToHashSet();

        return _plans
            .Where(p => p.Intent == analysis.Intent)
            .Select(p =>
            {
                double score = 0;
                if (p.Category == analysis.Category) score += 2;

                var goalWords = p.Goal.ToLowerInvariant()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 2).ToHashSet();
                score += queryWords.Intersect(goalWords).Count();

                score += Math.Min(p.UseCount * 0.3, 2.0);
                return (plan: p, score);
            })
            .Where(x => x.score >= 3.0)
            .OrderByDescending(x => x.score)
            .Select(x => x.plan)
            .FirstOrDefault();
    }

    private static string? GetObservationAfter(List<AgentStep> steps, AgentStep actionStep)
    {
        var idx = steps.IndexOf(actionStep);
        if (idx < 0 || idx + 1 >= steps.Count) return null;
        var next = steps[idx + 1];
        return next.Type == AgentStepType.Observation
            ? Truncate(next.Content, 150) : null;
    }

    private void PruneOldPlans()
    {
        if (_plans.Count > MaxPlans)
            _plans = _plans
                .OrderByDescending(p => p.UseCount)
                .ThenByDescending(p => p.LastUsed)
                .Take(MaxPlans)
                .ToList();
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom == 0 ? 0 : dot / denom;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public class StoredPlan
{
    public string Id { get; set; } = "";
    public string Goal { get; set; } = "";
    public string? Intent { get; set; }
    public string? Category { get; set; }
    public List<StoredSkillCall> SkillChain { get; set; } = [];
    public string FinalAnswerSummary { get; set; } = "";
    public float[]? GoalEmbedding { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsed { get; set; }
    public int UseCount { get; set; }
    public double AvgQualityScore { get; set; }
}

public class StoredSkillCall
{
    public string SkillName { get; set; } = "";
    public Dictionary<string, object?> Parameters { get; set; } = new();
    public string? ObservationSummary { get; set; }
}
