using System.Text.Json;

namespace RevitChatBot.Core.Learning;

/// <summary>
/// Learns user annotation preferences from repeated interactions.
/// After 3+ annotation operations with similar patterns, promotes them
/// to reusable templates. Templates are suggested to the user for
/// consistent annotation across the project.
///
/// Integrates with LearningCortex via the ProactiveSuggestionEngine.
/// Data persists in learning_cortex/annotation_templates.json.
/// </summary>
public class AnnotationTemplateLearner
{
    private readonly string _dataPath;
    private readonly List<AnnotationRecord> _records = [];
    private readonly List<AnnotationTemplate> _templates = [];
    private readonly object _lock = new();

    public AnnotationTemplateLearner(string dataDir)
    {
        _dataPath = Path.Combine(dataDir, "annotation_templates.json");
        Directory.CreateDirectory(dataDir);
    }

    /// <summary>
    /// Record an annotation operation and its parameters.
    /// Called after place_tags, arrange_tags, auto_dimension, smart_annotate complete.
    /// </summary>
    public void RecordAnnotation(AnnotationRecord record)
    {
        lock (_lock)
        {
            _records.Add(record);
            TryPromoteTemplate();
        }
    }

    /// <summary>
    /// Get all promoted templates.
    /// </summary>
    public List<AnnotationTemplate> GetTemplates()
    {
        lock (_lock) { return [.. _templates]; }
    }

    /// <summary>
    /// Find the best matching template for a given context.
    /// </summary>
    public AnnotationTemplate? FindTemplate(string? viewType = null, string? category = null)
    {
        lock (_lock)
        {
            return _templates
                .Where(t =>
                    (viewType is null || t.ViewType == viewType) &&
                    (category is null || t.Categories.Contains(category, StringComparer.OrdinalIgnoreCase)))
                .OrderByDescending(t => t.TimesUsed)
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// Build a context string for the agent prompt summarizing annotation preferences.
    /// </summary>
    public string BuildAnnotationContext(int maxChars = 500)
    {
        lock (_lock)
        {
            if (_templates.Count == 0 && _records.Count < 3)
                return "";

            var lines = new List<string>
            {
                "--- ANNOTATION PREFERENCES (learned) ---"
            };

            foreach (var t in _templates.OrderByDescending(t => t.TimesUsed).Take(3))
            {
                lines.Add($"  • {t.Name}: categories={string.Join(",", t.Categories)}, " +
                    $"position={t.PreferredPosition}, leader={t.AddLeader}, " +
                    $"auto_arrange={t.AutoArrange} (used {t.TimesUsed}x)");
            }

            if (_records.Count >= 3)
            {
                var topCategory = _records
                    .GroupBy(r => r.Category)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key;
                if (topCategory is not null)
                    lines.Add($"  • Most tagged category: {topCategory}");

                var topPosition = _records
                    .Where(r => r.PreferredPosition is not null)
                    .GroupBy(r => r.PreferredPosition)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key;
                if (topPosition is not null)
                    lines.Add($"  • Preferred tag position: {topPosition}");
            }

            var result = string.Join("\n", lines);
            return result.Length > maxChars ? result[..maxChars] : result;
        }
    }

    private void TryPromoteTemplate()
    {
        if (_records.Count < 3) return;

        var groups = _records
            .GroupBy(r => new { r.SkillName, r.Category, r.PreferredPosition, r.ViewType })
            .Where(g => g.Count() >= 3);

        foreach (var group in groups)
        {
            var key = $"{group.Key.SkillName}_{group.Key.Category}_{group.Key.ViewType}";
            if (_templates.Any(t => t.Key == key)) continue;

            var sample = group.First();
            _templates.Add(new AnnotationTemplate
            {
                Key = key,
                Name = $"Auto: {group.Key.SkillName} for {group.Key.Category} in {group.Key.ViewType}",
                Categories = group.Select(r => r.Category).Distinct().ToList(),
                PreferredPosition = group.Key.PreferredPosition ?? "auto",
                AddLeader = group.Any(r => r.AddLeader),
                AutoArrange = group.Any(r => r.AutoArrange),
                ViewType = group.Key.ViewType ?? "FloorPlan",
                TimesUsed = group.Count(),
                CreatedUtc = DateTime.UtcNow
            });
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        List<AnnotationRecord> records;
        List<AnnotationTemplate> templates;
        lock (_lock)
        {
            records = [.. _records];
            templates = [.. _templates];
        }

        var data = new { records = records.TakeLast(200).ToList(), templates };
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_dataPath, json, ct);
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_dataPath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(_dataPath, ct);
            var data = JsonSerializer.Deserialize<AnnotationData>(json);
            if (data is null) return;

            lock (_lock)
            {
                _records.Clear();
                _records.AddRange(data.Records ?? []);
                _templates.Clear();
                _templates.AddRange(data.Templates ?? []);
            }
        }
        catch { }
    }

    private class AnnotationData
    {
        public List<AnnotationRecord>? Records { get; set; }
        public List<AnnotationTemplate>? Templates { get; set; }
    }
}

public class AnnotationRecord
{
    public string SkillName { get; set; } = "";
    public string Category { get; set; } = "";
    public string? PreferredPosition { get; set; }
    public bool AddLeader { get; set; }
    public bool AutoArrange { get; set; }
    public string? ViewType { get; set; }
    public int ElementsProcessed { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public class AnnotationTemplate
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Categories { get; set; } = [];
    public string PreferredPosition { get; set; } = "auto";
    public bool AddLeader { get; set; }
    public bool AutoArrange { get; set; }
    public string ViewType { get; set; } = "FloorPlan";
    public int TimesUsed { get; set; }
    public DateTime CreatedUtc { get; set; }
}
