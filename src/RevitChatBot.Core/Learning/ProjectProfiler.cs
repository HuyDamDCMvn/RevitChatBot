using System.Text.Json;
using System.Text.RegularExpressions;

namespace RevitChatBot.Core.Learning;

/// <summary>
/// Learns patterns specific to the current Revit project/model.
/// Extracts signals from skill observations, element queries, and codegen results
/// to build a growing understanding of:
///   - Dominant MEP systems (HVAC-heavy? plumbing-heavy? electrical?)
///   - Common family names and naming conventions
///   - Recurring issues (e.g., "Level 3 always has clearance violations")
///   - Model-specific parameters and shared parameters in use
///   - System sizing patterns (typical duct sizes, pipe diameters)
/// 
/// This profile helps the agent prioritize relevant skills and tailor responses.
/// </summary>
public partial class ProjectProfiler
{
    private readonly string _filePath;
    private ProjectProfile _profile = new();

    public ProjectProfiler(string dataDir)
    {
        _filePath = Path.Combine(dataDir, "project_profile.json");
    }

    public ProjectProfile GetProfile() => _profile;

    /// <summary>
    /// Extract patterns from skill observation text.
    /// Called after each skill execution with the observation result.
    /// Uses lightweight regex extraction — no LLM needed.
    /// </summary>
    public void IngestFromObservations(List<string> observations, string? category)
    {
        if (category is not null)
            IncrementCounter(_profile.CategoryFrequency, category);

        foreach (var obs in observations)
        {
            ExtractFamilyNames(obs);
            ExtractLevelPatterns(obs);
            ExtractSizingPatterns(obs);
            ExtractIssuePatterns(obs);
            ExtractParameterNames(obs);
        }

        _profile.LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Directly record a project fact (e.g., from context scanning).
    /// </summary>
    public void RecordFact(string factKey, string factValue)
    {
        _profile.KnownFacts[factKey] = factValue;
        _profile.LastUpdated = DateTime.UtcNow;
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(_profile, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch { /* non-critical */ }
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, ct);
                _profile = JsonSerializer.Deserialize<ProjectProfile>(json, JsonOpts) ?? new();
            }
        }
        catch { _profile = new(); }
    }

    private void ExtractFamilyNames(string text)
    {
        var matches = FamilyNameRegex().Matches(text);
        foreach (Match m in matches)
        {
            var name = m.Groups[1].Value.Trim();
            if (name.Length >= 3 && name.Length <= 80)
                IncrementCounter(_profile.FamilyNameFrequency, name);
        }
    }

    private void ExtractLevelPatterns(string text)
    {
        var matches = LevelRegex().Matches(text);
        foreach (Match m in matches)
        {
            var level = m.Groups[0].Value.Trim();
            IncrementCounter(_profile.LevelFrequency, level);
        }
    }

    private void ExtractSizingPatterns(string text)
    {
        var matches = SizingRegex().Matches(text);
        foreach (Match m in matches)
        {
            var size = m.Groups[0].Value.Trim();
            IncrementCounter(_profile.SizingFrequency, size);
        }
    }

    private void ExtractIssuePatterns(string text)
    {
        var lowerText = text.ToLowerInvariant();

        foreach (var keyword in IssueKeywords)
        {
            if (lowerText.Contains(keyword))
            {
                var context = ExtractSurroundingContext(lowerText, keyword, 60);
                IncrementCounter(_profile.IssueFrequency, context);
            }
        }
    }

    private void ExtractParameterNames(string text)
    {
        var matches = ParameterRegex().Matches(text);
        foreach (Match m in matches)
        {
            var param = m.Groups[1].Value.Trim();
            if (param.Length >= 2 && param.Length <= 60)
                IncrementCounter(_profile.ParameterFrequency, param);
        }
    }

    private static string ExtractSurroundingContext(string text, string keyword, int windowSize)
    {
        var idx = text.IndexOf(keyword, StringComparison.Ordinal);
        if (idx < 0) return keyword;
        var start = Math.Max(0, idx - windowSize / 2);
        var end = Math.Min(text.Length, idx + keyword.Length + windowSize / 2);
        return text[start..end].Trim();
    }

    private static void IncrementCounter(Dictionary<string, int> dict, string key)
    {
        dict.TryGetValue(key, out var count);
        dict[key] = count + 1;
    }

    private static readonly string[] IssueKeywords =
    [
        "clash", "violation", "warning", "error", "fail",
        "insufficient", "undersized", "oversized", "missing",
        "not connected", "disconnected", "clearance"
    ];

    [GeneratedRegex(@"Family:\s*(.+?)(?:\s*$|\s*,|\s*\|)", RegexOptions.Multiline)]
    private static partial Regex FamilyNameRegex();

    [GeneratedRegex(@"Level\s+\d+|Level:\s*.+?(?:,|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex LevelRegex();

    [GeneratedRegex(@"\d+(?:\.\d+)?\s*(?:mm|inch|in|"")\s*(?:x\s*\d+(?:\.\d+)?\s*(?:mm|inch|in|""))?", RegexOptions.IgnoreCase)]
    private static partial Regex SizingRegex();

    [GeneratedRegex(@"(?:parameter|param)[\s:]+[""']?(\w[\w\s]{1,58}\w)[""']?", RegexOptions.IgnoreCase)]
    private static partial Regex ParameterRegex();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

/// <summary>
/// Accumulated project-specific knowledge. Persisted to disk.
/// </summary>
public class ProjectProfile
{
    public Dictionary<string, int> CategoryFrequency { get; set; } = new();
    public Dictionary<string, int> FamilyNameFrequency { get; set; } = new();
    public Dictionary<string, int> LevelFrequency { get; set; } = new();
    public Dictionary<string, int> SizingFrequency { get; set; } = new();
    public Dictionary<string, int> IssueFrequency { get; set; } = new();
    public Dictionary<string, int> ParameterFrequency { get; set; } = new();
    public Dictionary<string, string> KnownFacts { get; set; } = new();
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// Derive the dominant MEP discipline for this project.
    /// </summary>
    public string DominantDiscipline
    {
        get
        {
            if (CategoryFrequency.Count == 0) return "unknown";
            return CategoryFrequency.MaxBy(kv => kv.Value).Key;
        }
    }

    /// <summary>
    /// Get patterns as human-readable strings for prompt injection.
    /// </summary>
    public List<string> KnownPatterns
    {
        get
        {
            var patterns = new List<string>();

            if (CategoryFrequency.Count > 0)
            {
                var top = CategoryFrequency.OrderByDescending(kv => kv.Value)
                    .Take(3).Select(kv => $"{kv.Key}({kv.Value}x)");
                patterns.Add($"Dominant disciplines: {string.Join(", ", top)}");
            }

            var topFamilies = FamilyNameFrequency
                .OrderByDescending(kv => kv.Value).Take(5)
                .Where(kv => kv.Value >= 2)
                .Select(kv => kv.Key)
                .ToList();
            if (topFamilies.Count > 0)
                patterns.Add($"Common families: {string.Join(", ", topFamilies)}");

            var topIssues = IssueFrequency
                .OrderByDescending(kv => kv.Value).Take(3)
                .Where(kv => kv.Value >= 2)
                .Select(kv => $"{kv.Key} ({kv.Value}x)")
                .ToList();
            if (topIssues.Count > 0)
                patterns.Add($"Recurring issues: {string.Join("; ", topIssues)}");

            var topSizes = SizingFrequency
                .OrderByDescending(kv => kv.Value).Take(3)
                .Where(kv => kv.Value >= 2)
                .Select(kv => kv.Key)
                .ToList();
            if (topSizes.Count > 0)
                patterns.Add($"Common sizing: {string.Join(", ", topSizes)}");

            foreach (var (key, value) in KnownFacts)
                patterns.Add($"{key}: {value}");

            return patterns;
        }
    }
}
