using System.Text.Json;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// Learns from successful skill invocations and stores them as few-shot examples.
/// Prioritizes learned examples over static ones in FewShotIntentLibrary.
/// Persists to disk for cross-session learning.
/// </summary>
public class AdaptiveFewShotLearning
{
    private readonly string _filePath;
    private List<LearnedExample> _examples = [];
    private const int MaxExamples = 200;

    public AdaptiveFewShotLearning(string filePath)
    {
        _filePath = filePath;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, ct);
                _examples = JsonSerializer.Deserialize<List<LearnedExample>>(json) ?? [];
            }
        }
        catch { _examples = []; }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_examples, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch { }
    }

    /// <summary>
    /// Record a successful skill invocation as a learned example.
    /// </summary>
    public void RecordSuccess(string userQuery, string skillName, Dictionary<string, object?> parameters)
    {
        var existing = _examples.FirstOrDefault(e =>
            e.SkillName == skillName &&
            e.Query.Equals(userQuery, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.SuccessCount++;
            existing.LastUsed = DateTime.UtcNow;
            return;
        }

        var analysis = new QueryAnalysis
        {
            OriginalQuery = userQuery,
            Intent = MepGlossary.DetectIntent(userQuery),
            Category = MepGlossary.DetectCategory(userQuery),
            Language = MepGlossary.DetectLanguage(userQuery)
        };

        var paramStr = string.Join(", ", parameters
            .Where(p => p.Value != null)
            .Select(p => $"{p.Key}=\"{p.Value}\""));

        _examples.Add(new LearnedExample
        {
            Query = userQuery,
            Intent = analysis.Intent,
            Category = analysis.Category,
            SkillName = skillName,
            Parameters = paramStr,
            SuccessCount = 1,
            CreatedAt = DateTime.UtcNow,
            LastUsed = DateTime.UtcNow
        });

        if (_examples.Count > MaxExamples)
        {
            _examples = _examples
                .OrderByDescending(e => e.SuccessCount)
                .ThenByDescending(e => e.LastUsed)
                .Take(MaxExamples)
                .ToList();
        }
    }

    /// <summary>
    /// Get learned examples relevant to the given query analysis.
    /// These are prioritized over static FewShotIntentLibrary examples.
    /// </summary>
    public List<FewShotExample> GetLearnedExamples(QueryAnalysis analysis, int maxCount = 3)
    {
        return _examples
            .Select(e =>
            {
                double score = 0;
                if (e.Intent == analysis.Intent) score += 3.0;
                if (analysis.Category != null && e.Category == analysis.Category) score += 2.0;
                score += Math.Min(e.SuccessCount * 0.5, 3.0);
                if ((DateTime.UtcNow - e.LastUsed).TotalDays < 7) score += 1.0;
                return (example: e, score);
            })
            .Where(x => x.score >= 2.0)
            .OrderByDescending(x => x.score)
            .Take(maxCount)
            .Select(x => new FewShotExample(
                x.example.Query,
                x.example.Intent,
                x.example.Category,
                x.example.SkillName,
                x.example.Parameters))
            .ToList();
    }

    public int Count => _examples.Count;
}

public class LearnedExample
{
    public string Query { get; set; } = "";
    public string Intent { get; set; } = "";
    public string? Category { get; set; }
    public string SkillName { get; set; } = "";
    public string Parameters { get; set; } = "";
    public int SuccessCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsed { get; set; }
}
