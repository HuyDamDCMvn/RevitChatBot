using System.Text.Json;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// Accumulates improvement suggestions from SelfEvaluator across sessions.
/// Injects "lessons learned" into the system prompt for continuous improvement.
/// </summary>
public class ImprovementStore
{
    private readonly string _filePath;
    private List<ImprovementEntry> _entries = [];
    private bool _loaded;
    private const int MaxEntries = 100;

    public ImprovementStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_loaded) return;
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, ct);
                _entries = JsonSerializer.Deserialize<List<ImprovementEntry>>(json, JsonOpts) ?? [];
            }
        }
        catch { _entries = []; }
        _loaded = true;
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_entries, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch { /* non-critical */ }
    }

    /// <summary>
    /// Record an improvement suggestion from self-evaluation.
    /// qualityDelta > 0 means the suggestion improved quality.
    /// </summary>
    public void RecordImprovement(string intent, string suggestion, double qualityDelta)
    {
        if (string.IsNullOrWhiteSpace(suggestion)) return;

        var existing = _entries.FirstOrDefault(e =>
            e.Intent == intent && e.Suggestion.Equals(suggestion, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.OccurrenceCount++;
            existing.AvgQualityDelta =
                (existing.AvgQualityDelta * (existing.OccurrenceCount - 1) + qualityDelta) /
                existing.OccurrenceCount;
            existing.LastSeen = DateTime.UtcNow;
            return;
        }

        _entries.Add(new ImprovementEntry
        {
            Intent = intent,
            Suggestion = suggestion,
            AvgQualityDelta = qualityDelta,
            OccurrenceCount = 1,
            CreatedAt = DateTime.UtcNow,
            LastSeen = DateTime.UtcNow
        });

        Prune();
    }

    /// <summary>
    /// Get improvement hints relevant to the given intent for prompt injection.
    /// </summary>
    public string GetImprovementHints(string intent, int maxHints = 5)
    {
        var relevant = _entries
            .Where(e => e.Intent == intent || e.OccurrenceCount >= 3)
            .OrderByDescending(e => e.OccurrenceCount * (1 + e.AvgQualityDelta))
            .Take(maxHints)
            .ToList();

        if (relevant.Count == 0) return "";

        var lines = new List<string> { "--- LESSONS LEARNED (from self-evaluation) ---" };
        foreach (var e in relevant)
            lines.Add($"  - [{e.Intent}] {e.Suggestion} (seen {e.OccurrenceCount}x)");
        return string.Join("\n", lines);
    }

    public int Count => _entries.Count;

    private void Prune()
    {
        if (_entries.Count > MaxEntries)
            _entries = _entries
                .OrderByDescending(e => e.OccurrenceCount)
                .ThenByDescending(e => e.LastSeen)
                .Take(MaxEntries)
                .ToList();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public class ImprovementEntry
{
    public string Intent { get; set; } = "";
    public string Suggestion { get; set; } = "";
    public double AvgQualityDelta { get; set; }
    public int OccurrenceCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeen { get; set; }
}
