using System.Text.Json;
using RevitChatBot.Core.Learning;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// Per-project glossary that learns new terms from user interactions.
/// Extends MepGlossary with project-specific terminology (Family names, system abbreviations).
/// Also integrates model_inventory Family/Type names for normalization.
/// Publishes glossary_updated events via LearningModuleHub when new terms are added.
/// </summary>
public class DynamicGlossary
{
    private readonly string _filePath;
    private Dictionary<string, string> _projectTerms = new(StringComparer.OrdinalIgnoreCase);
    private LearningModuleHub? _hub;

    public DynamicGlossary(string filePath)
    {
        _filePath = filePath;
    }

    public void SetHub(LearningModuleHub hub) => _hub = hub;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, ct);
                _projectTerms = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                    ?? new(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { _projectTerms = new(StringComparer.OrdinalIgnoreCase); }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_projectTerms, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch { }
    }

    /// <summary>
    /// Register a project-specific term mapping.
    /// Publishes glossary_updated if the term is new.
    /// </summary>
    public void AddTerm(string term, string meaning)
    {
        bool isNew = !_projectTerms.ContainsKey(term);
        _projectTerms[term] = meaning;
        if (isNew)
        {
            _hub?.Publish(new LearningEvent("DynamicGlossary",
                LearningEventTypes.GlossaryUpdated,
                new { Term = term, Meaning = meaning, TotalTerms = _projectTerms.Count }));
        }
    }

    /// <summary>
    /// Bulk-register Family/Type names from model inventory context.
    /// Called once after model inventory is gathered.
    /// </summary>
    public void RegisterModelTerms(string modelInventoryContext)
    {
        if (string.IsNullOrWhiteSpace(modelInventoryContext)) return;

        var lines = modelInventoryContext.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim().TrimStart('-', '*', ' ');
            if (trimmed.Length < 3 || trimmed.Length > 80) continue;

            if (trimmed.Contains("Family:", StringComparison.OrdinalIgnoreCase))
            {
                var familyName = trimmed.Replace("Family:", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (familyName.Length >= 3)
                    _projectTerms.TryAdd(familyName, $"Revit Family: {familyName}");
            }
            else if (trimmed.Contains("Type:", StringComparison.OrdinalIgnoreCase))
            {
                var typeName = trimmed.Replace("Type:", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (typeName.Length >= 3)
                    _projectTerms.TryAdd(typeName, $"Revit Type: {typeName}");
            }
        }
    }

    /// <summary>
    /// Detect user queries containing unknown terms and auto-learn from corrections.
    /// </summary>
    public void LearnFromCorrection(string userMessage, string assistantResponse)
    {
        var correctionPatterns = new[]
        {
            "không phải", "thực ra", "ý tôi là", "tôi muốn nói",
            "actually", "I meant", "I mean", "no, the"
        };

        if (!correctionPatterns.Any(p =>
            userMessage.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return;

        var words = userMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            if (word.Length >= 4 && !MepGlossary.DetectCategory(word).HasValue()
                && !_projectTerms.ContainsKey(word))
            {
                var category = MepGlossary.DetectCategory(assistantResponse);
                if (category != null)
                {
                    _projectTerms[word] = category;
                    _hub?.Publish(new LearningEvent("DynamicGlossary",
                        LearningEventTypes.GlossaryUpdated,
                        new { Term = word, Meaning = category, Source = "correction" }));
                }
            }
        }
    }

    /// <summary>
    /// Normalize query using both static MepGlossary and dynamic project terms.
    /// </summary>
    public string NormalizeQuery(string query)
    {
        var result = MepGlossary.NormalizeQuery(query);

        foreach (var (term, meaning) in _projectTerms.OrderByDescending(kv => kv.Key.Length))
        {
            if (result.Contains(term, StringComparison.OrdinalIgnoreCase))
                result = result.Replace(term, meaning, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    public int TermCount => _projectTerms.Count;
}

internal static class NullableStringExtensions
{
    public static bool HasValue(this string? s) => !string.IsNullOrEmpty(s);
}
