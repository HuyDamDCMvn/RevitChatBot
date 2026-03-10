using System.Text.Json;

namespace RevitChatBot.Core.CodeGen;

/// <summary>
/// Module 1: Persists successful codegen results for reuse.
/// When the LLM generates code that compiles and runs successfully,
/// the code + metadata is saved. On subsequent similar requests,
/// the library is searched first to avoid regenerating code.
/// </summary>
public class CodeGenLibrary
{
    private readonly string _filePath;
    private List<CodeGenEntry> _entries = [];
    private bool _loaded;

    public CodeGenLibrary(string filePath)
    {
        _filePath = filePath;
    }

    public IReadOnlyList<CodeGenEntry> Entries => _entries.AsReadOnly();

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_loaded) return;
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, ct);
                _entries = JsonSerializer.Deserialize<List<CodeGenEntry>>(json, JsonOpts) ?? [];
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
    /// Record a successful code execution for future reuse.
    /// </summary>
    public void RecordSuccess(string description, string code, string output, double executionMs)
    {
        var keywords = ExtractKeywords(description);

        var existing = _entries.FindIndex(e =>
            e.Description.Equals(description, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            _entries[existing].Code = code;
            _entries[existing].LastOutput = output;
            _entries[existing].UseCount++;
            _entries[existing].LastUsed = DateTime.UtcNow;
            _entries[existing].AvgExecutionMs =
                (_entries[existing].AvgExecutionMs * (_entries[existing].UseCount - 1) + executionMs) /
                _entries[existing].UseCount;
            return;
        }

        _entries.Add(new CodeGenEntry
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Description = description,
            Keywords = keywords,
            Code = code,
            LastOutput = output,
            UseCount = 1,
            CreatedAt = DateTime.UtcNow,
            LastUsed = DateTime.UtcNow,
            AvgExecutionMs = executionMs
        });

        if (_entries.Count > 200)
            _entries = _entries.OrderByDescending(e => e.UseCount)
                .ThenByDescending(e => e.LastUsed)
                .Take(150).ToList();
    }

    /// <summary>
    /// Record a failed execution to track error patterns.
    /// </summary>
    public void RecordFailure(string description, string code, string error)
    {
        var existing = _entries.FindIndex(e =>
            e.Code == code || e.Description.Equals(description, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            _entries[existing].FailCount++;
            _entries[existing].LastError = error;
            if (_entries[existing].FailCount > 5 && _entries[existing].UseCount == 0)
                _entries.RemoveAt(existing);
        }
    }

    /// <summary>
    /// Search for a previously successful code execution that matches the query.
    /// Returns the best match if found (keyword-based scoring).
    /// </summary>
    public CodeGenEntry? FindMatch(string query)
    {
        if (_entries.Count == 0) return null;

        var queryKeywords = ExtractKeywords(query);
        if (queryKeywords.Count == 0) return null;

        CodeGenEntry? best = null;
        double bestScore = 0;

        foreach (var entry in _entries)
        {
            if (entry.UseCount == 0 && entry.FailCount > 0) continue;

            double score = CalculateMatchScore(queryKeywords, entry.Keywords, entry);
            if (score > bestScore)
            {
                bestScore = score;
                best = entry;
            }
        }

        return bestScore >= 0.5 ? best : null;
    }

    /// <summary>
    /// Get a summary of the library for injection into LLM context.
    /// Shows the most-used saved code snippets so the LLM knows what's available.
    /// </summary>
    public string GetLibrarySummary(int maxEntries = 15)
    {
        if (_entries.Count == 0) return "";

        var top = _entries
            .Where(e => e.UseCount > 0)
            .OrderByDescending(e => e.UseCount)
            .ThenByDescending(e => e.LastUsed)
            .Take(maxEntries);

        var lines = new List<string> { "[saved_codegen_library]" };
        foreach (var e in top)
        {
            lines.Add($"  - \"{e.Description}\" (used {e.UseCount}x, " +
                $"avg {Math.Round(e.AvgExecutionMs)}ms" +
                (e.IsPromotedToSkill ? ", SKILL" : "") + ")");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Mark an entry as promoted to a dynamic skill (so we know not to duplicate).
    /// </summary>
    public void MarkAsPromoted(string entryId, string skillName)
    {
        var entry = _entries.Find(e => e.Id == entryId);
        if (entry != null)
        {
            entry.IsPromotedToSkill = true;
            entry.PromotedSkillName = skillName;
        }
    }

    private static double CalculateMatchScore(
        List<string> queryKw, List<string> entryKw, CodeGenEntry entry)
    {
        if (entryKw.Count == 0) return 0;
        int matches = queryKw.Count(qk => entryKw.Any(ek =>
            ek.Equals(qk, StringComparison.OrdinalIgnoreCase) ||
            ek.Contains(qk, StringComparison.OrdinalIgnoreCase) ||
            qk.Contains(ek, StringComparison.OrdinalIgnoreCase)));

        double keywordScore = (double)matches / Math.Max(queryKw.Count, 1);
        double usageBonus = Math.Min(entry.UseCount * 0.05, 0.3);
        double recencyBonus = entry.LastUsed > DateTime.UtcNow.AddDays(-7) ? 0.1 : 0;

        return keywordScore + usageBonus + recencyBonus;
    }

    private static List<string> ExtractKeywords(string text)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did", "will", "would", "could",
            "should", "may", "might", "can", "shall", "and", "or", "but", "if",
            "then", "else", "when", "where", "how", "what", "which", "who", "whom",
            "this", "that", "these", "those", "for", "with", "from", "by", "at",
            "in", "on", "to", "of", "all", "each", "every", "both", "few", "more",
            "most", "other", "some", "such", "no", "not", "only", "own", "so",
            "than", "too", "very", "just", "about", "it", "its", "my", "me",
            "tất", "cả", "các", "của", "và", "cho", "trong", "với", "là",
            "này", "đó", "được", "có", "không", "những", "một", "mỗi",
            "hãy", "đi", "xem", "kiểm", "tra"
        };

        return text.ToLowerInvariant()
            .Split([' ', ',', '.', '?', '!', ':', ';', '(', ')', '[', ']', '{', '}',
                    '\n', '\r', '\t', '"', '\'', '/', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Distinct()
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public class CodeGenEntry
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Keywords { get; set; } = [];
    public string Code { get; set; } = "";
    public string? LastOutput { get; set; }
    public string? LastError { get; set; }
    public int UseCount { get; set; }
    public int FailCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsed { get; set; }
    public double AvgExecutionMs { get; set; }
    public bool IsPromotedToSkill { get; set; }
    public string? PromotedSkillName { get; set; }
}
