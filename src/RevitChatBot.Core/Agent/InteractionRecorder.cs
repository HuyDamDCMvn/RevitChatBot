using System.Text.Json;
using RevitChatBot.Core.Learning;
using RevitChatBot.Core.LLM;

namespace RevitChatBot.Core.Agent;

/// <summary>
/// Records completed interactions (goal + skills used + answer) for
/// knowledge synthesis, gap analysis, and composite skill discovery.
/// Persists to JSONL for efficient append-only storage.
/// </summary>
public class InteractionRecorder
{
    private readonly string _filePath;
    private List<InteractionRecord> _records = [];
    private bool _loaded;

    private const int MaxRecords = 500;
    private const int SynthesisThreshold = 20;

    public InteractionRecorder(string filePath)
    {
        _filePath = filePath;
    }

    public IReadOnlyList<InteractionRecord> Records => _records.AsReadOnly();

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_loaded) return;
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, ct);
                _records = JsonSerializer.Deserialize<List<InteractionRecord>>(json, JsonOpts) ?? [];
            }
        }
        catch { _records = []; }
        _loaded = true;
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_records, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch { /* non-critical */ }
    }

    public void Record(AgentPlan plan, QueryAnalysis? analysis)
    {
        if (!plan.IsCompleted || plan.FinalAnswer == null) return;

        _records.Add(new InteractionRecord
        {
            Query = plan.Goal,
            Answer = Truncate(plan.FinalAnswer, 500),
            Topic = analysis?.Category ?? analysis?.Intent,
            Intent = analysis?.Intent,
            SkillsUsed = plan.Steps
                .Where(s => s.Type == AgentStepType.Action && s.SkillName != null)
                .Select(s => s.SkillName!)
                .Distinct()
                .ToList(),
            StepCount = plan.Steps.Count,
            Timestamp = DateTime.UtcNow
        });

        Prune();
    }

    /// <summary>
    /// Record from a hub PlanCompletedData event (hub-driven, no AgentPlan dependency).
    /// </summary>
    public void RecordFromEvent(Learning.PlanCompletedData data)
    {
        if (string.IsNullOrWhiteSpace(data.Goal)) return;

        var existing = _records.Any(r =>
            r.Query == data.Goal && r.Timestamp > DateTime.UtcNow.AddSeconds(-5));
        if (existing) return;

        _records.Add(new InteractionRecord
        {
            Query = data.Goal,
            Answer = Truncate(data.FinalAnswer ?? "", 500),
            Topic = data.Category ?? data.Intent,
            Intent = data.Intent,
            SkillsUsed = data.SkillsUsed,
            StepCount = data.StepCount,
            Timestamp = DateTime.UtcNow
        });

        Prune();
    }

    /// <summary>
    /// Whether enough new interactions have accumulated to trigger knowledge synthesis.
    /// </summary>
    public bool ShouldSynthesize =>
        _records.Count(r => r.Timestamp > DateTime.UtcNow.AddDays(-1)) >= SynthesisThreshold;

    public List<InteractionRecord> GetRecentRecords(int days = 7) =>
        _records.Where(r => r.Timestamp > DateTime.UtcNow.AddDays(-days)).ToList();

    /// <summary>
    /// Get queries that required codegen fallback (indicating skill gaps).
    /// </summary>
    public List<InteractionRecord> GetCodegenFallbacks(int days = 30) =>
        _records
            .Where(r => r.Timestamp > DateTime.UtcNow.AddDays(-days)
                        && r.SkillsUsed.Contains("execute_revit_code"))
            .ToList();

    /// <summary>
    /// Get repeated skill chain patterns (for composite skill discovery).
    /// </summary>
    public Dictionary<string, int> GetSkillChainFrequencies(int minCount = 2)
    {
        return _records
            .Where(r => r.SkillsUsed.Count >= 2)
            .GroupBy(r => string.Join("→", r.SkillsUsed))
            .Where(g => g.Count() >= minCount)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private void Prune()
    {
        if (_records.Count > MaxRecords)
            _records = _records
                .OrderByDescending(r => r.Timestamp)
                .Take(MaxRecords)
                .ToList();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public class InteractionRecord
{
    public string Query { get; set; } = "";
    public string Answer { get; set; } = "";
    public string? Topic { get; set; }
    public string? Intent { get; set; }
    public List<string> SkillsUsed { get; set; } = [];
    public int StepCount { get; set; }
    public DateTime Timestamp { get; set; }
}
