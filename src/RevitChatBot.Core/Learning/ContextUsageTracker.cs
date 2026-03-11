using System.Text.Json;

namespace RevitChatBot.Core.Learning;

/// <summary>
/// Tracks which context keys the LLM actually references in its responses.
/// After each interaction, analyzes the response to determine which context
/// sections were useful vs ignored. Feeds priority adjustments back to
/// ContextWindowOptimizer for smarter context selection in future queries.
/// </summary>
public class ContextUsageTracker
{
    private readonly string _filePath;
    private readonly Dictionary<string, ContextUsageStats> _stats = new();
    private readonly object _lock = new();

    public ContextUsageTracker(string dataDir)
    {
        _filePath = Path.Combine(dataDir, "context_usage.json");
        Directory.CreateDirectory(dataDir);
    }

    /// <summary>
    /// After each agent response, analyze which context keys were referenced.
    /// </summary>
    public void TrackUsage(
        IReadOnlyCollection<string> contextKeys,
        string response,
        string? intent = null)
    {
        if (contextKeys.Count == 0 || string.IsNullOrWhiteSpace(response)) return;

        var responseLower = response.ToLowerInvariant();

        lock (_lock)
        {
            foreach (var key in contextKeys)
            {
                if (!_stats.TryGetValue(key, out var stat))
                {
                    stat = new ContextUsageStats { Key = key };
                    _stats[key] = stat;
                }

                stat.TotalOffered++;
                if (WasReferenced(key, responseLower))
                    stat.TotalReferenced++;

                if (intent is not null)
                {
                    stat.IntentCounts.TryGetValue(intent, out var ic);
                    stat.IntentCounts[intent] = ic + 1;
                }
            }
        }
    }

    /// <summary>
    /// Get priority adjustments: keys with high reference rate get boosted,
    /// keys with low reference rate get deprioritized.
    /// </summary>
    public Dictionary<string, double> GetPriorityAdjustments(int minSamples = 5)
    {
        lock (_lock)
        {
            var adjustments = new Dictionary<string, double>();
            foreach (var (key, stat) in _stats)
            {
                if (stat.TotalOffered < minSamples) continue;
                var refRate = (double)stat.TotalReferenced / stat.TotalOffered;
                adjustments[key] = refRate switch
                {
                    >= 0.7 => 1.5,
                    >= 0.4 => 1.0,
                    >= 0.2 => 0.7,
                    _ => 0.4
                };
            }
            return adjustments;
        }
    }

    /// <summary>
    /// Build a context string summarizing which context sections are most useful.
    /// </summary>
    public string BuildUsageSummary(int maxEntries = 5)
    {
        lock (_lock)
        {
            var ranked = _stats.Values
                .Where(s => s.TotalOffered >= 3)
                .OrderByDescending(s => s.ReferenceRate)
                .Take(maxEntries)
                .ToList();

            if (ranked.Count == 0) return "";

            var lines = new List<string> { "--- CONTEXT EFFECTIVENESS (learned) ---" };
            foreach (var s in ranked)
            {
                lines.Add($"  • [{s.Key}]: referenced {s.ReferenceRate:P0} of the time " +
                    $"({s.TotalReferenced}/{s.TotalOffered})");
            }

            var lowUsage = _stats.Values
                .Where(s => s.TotalOffered >= 5 && s.ReferenceRate < 0.15)
                .Select(s => s.Key)
                .Take(3)
                .ToList();

            if (lowUsage.Count > 0)
                lines.Add($"  Low-usage: {string.Join(", ", lowUsage)} (consider trimming)");

            return string.Join("\n", lines);
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        Dictionary<string, ContextUsageStats> snapshot;
        lock (_lock) { snapshot = new(_stats); }

        try
        {
            var json = JsonSerializer.Serialize(snapshot, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch { /* non-critical */ }
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            var data = JsonSerializer.Deserialize<Dictionary<string, ContextUsageStats>>(json, JsonOpts);
            if (data is null) return;

            lock (_lock)
            {
                _stats.Clear();
                foreach (var (k, v) in data)
                    _stats[k] = v;
            }
        }
        catch { /* non-critical */ }
    }

    private static bool WasReferenced(string contextKey, string responseLower)
    {
        var keyTerms = GetKeyTerms(contextKey);
        int matches = keyTerms.Count(term => responseLower.Contains(term));
        return matches >= Math.Max(1, keyTerms.Count / 3);
    }

    private static List<string> GetKeyTerms(string contextKey) => contextKey switch
    {
        "project_info" => ["project", "dự án", "title", "tên"],
        "model_inventory" => ["family", "type", "element", "phần tử", "loại"],
        "active_view" => ["view", "floor plan", "section", "mặt bằng"],
        "selected_elements" => ["selected", "chọn", "selection"],
        "mep_systems" => ["system", "hệ thống", "duct", "pipe", "ống"],
        "room_spaces" => ["room", "space", "phòng", "không gian"],
        "system_detail" => ["connector", "flow", "lưu lượng", "áp suất"],
        "level_summary" => ["level", "tầng", "floor"],
        "knowledge_base" => ["standard", "tiêu chuẩn", "reference", "tham khảo"],
        "learned_facts" => ["learned", "fact", "ghi nhớ", "nhớ rằng"],
        "user_preferences" => ["preference", "prefer", "ưu tiên"],
        "conversation_memory" => ["previous", "trước đó", "earlier", "history"],
        _ => [contextKey.Replace("_", " ")]
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public class ContextUsageStats
{
    public string Key { get; set; } = "";
    public int TotalOffered { get; set; }
    public int TotalReferenced { get; set; }
    public double ReferenceRate => TotalOffered > 0 ? (double)TotalReferenced / TotalOffered : 0;
    public Dictionary<string, int> IntentCounts { get; set; } = new();
}
