using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitChatBot.Visualization.Learning;

/// <summary>
/// Self-learning engine that captures and analyzes patterns of how the agent
/// uses visualization alongside other skills. Over time, it learns:
///
/// 1. Which skill → visualization pairings are most effective
///    (e.g., "check_clearance always followed by highlight_elements" → auto-chain)
///
/// 2. Which severity colors the user responds to (implicit feedback via
///    follow-up questions or "clear" actions)
///
/// 3. Optimal visualization density (too many highlights = noisy, too few = unhelpful)
///
/// 4. Skill chain patterns that include visualization steps
///    (feeds into CompositeSkillEngine for auto-composition)
///
/// This creates a closed loop:
///   Agent runs skill → Visualizes result → User reacts → Agent learns
///   Next time: Agent auto-visualizes with learned preferences
/// </summary>
public class VisualFeedbackLearner
{
    private readonly string _dataPath;
    private readonly List<VisualSkillPairing> _pairings = [];
    private readonly Dictionary<string, VisualizationEffectiveness> _effectiveness = new();
    private readonly object _lock = new();

    public VisualFeedbackLearner(string dataDir)
    {
        _dataPath = Path.Combine(dataDir, "visual_feedback_learning.json");
        Directory.CreateDirectory(dataDir);
        Load();
    }

    /// <summary>
    /// Record that a skill was followed by a visualization action.
    /// Called by the agent orchestrator after each tool call.
    /// </summary>
    public void RecordSkillVisualizationPairing(
        string precedingSkillName,
        string visualizationAction,
        string severity,
        int elementCount,
        bool userClearedQuickly = false)
    {
        lock (_lock)
        {
            var key = $"{precedingSkillName}→{visualizationAction}";
            var existing = _pairings.FirstOrDefault(p => p.Key == key);

            if (existing is not null)
            {
                existing.Count++;
                existing.LastUsed = DateTime.UtcNow;
                existing.TotalElements += elementCount;
                if (userClearedQuickly) existing.QuickClearCount++;
                if (!existing.SeveritiesUsed.Contains(severity))
                    existing.SeveritiesUsed.Add(severity);
            }
            else
            {
                _pairings.Add(new VisualSkillPairing
                {
                    Key = key,
                    PrecedingSkill = precedingSkillName,
                    VisualizationAction = visualizationAction,
                    SeveritiesUsed = [severity],
                    Count = 1,
                    TotalElements = elementCount,
                    LastUsed = DateTime.UtcNow
                });
            }
        }
    }

    /// <summary>
    /// Record whether the user found the visualization helpful.
    /// Inferred from user behavior:
    /// - Positive: user asks follow-up about highlighted elements
    /// - Negative: user immediately clears or ignores
    /// - Neutral: user continues to next task without referencing visualization
    /// </summary>
    public void RecordEffectiveness(string skillName, VisualizationFeedback feedback)
    {
        lock (_lock)
        {
            if (!_effectiveness.TryGetValue(skillName, out var eff))
            {
                eff = new VisualizationEffectiveness { SkillName = skillName };
                _effectiveness[skillName] = eff;
            }

            switch (feedback)
            {
                case VisualizationFeedback.Positive:
                    eff.PositiveCount++;
                    break;
                case VisualizationFeedback.Negative:
                    eff.NegativeCount++;
                    break;
                case VisualizationFeedback.Neutral:
                    eff.NeutralCount++;
                    break;
            }
        }
    }

    /// <summary>
    /// Recommend whether the agent should auto-visualize after a given skill.
    /// Based on learned pairing frequency and effectiveness.
    /// Returns null if no recommendation (insufficient data).
    /// </summary>
    public VisualizationRecommendation? GetRecommendation(string skillName)
    {
        lock (_lock)
        {
            var relevantPairings = _pairings
                .Where(p => p.PrecedingSkill == skillName)
                .OrderByDescending(p => p.Count)
                .ToList();

            if (relevantPairings.Count == 0) return null;

            var best = relevantPairings.First();
            if (best.Count < 2) return null;

            _effectiveness.TryGetValue(skillName, out var eff);
            double score = eff?.EffectivenessScore ?? 0.5;

            if (score < 0.3) return null;

            var avgElements = best.TotalElements / Math.Max(1, best.Count);
            var quickClearRate = best.QuickClearCount / (double)Math.Max(1, best.Count);

            if (quickClearRate > 0.5) return null;

            return new VisualizationRecommendation
            {
                ShouldVisualize = true,
                VisualizationAction = best.VisualizationAction,
                RecommendedSeverity = best.MostFrequentSeverity,
                Confidence = Math.Min(1.0, best.Count / 10.0) * score,
                AvgElementCount = avgElements,
                Reason = $"Paired {best.Count}x with {best.PrecedingSkill}, " +
                         $"effectiveness: {score:F2}"
            };
        }
    }

    /// <summary>
    /// Get learned pairings as context for the agent's prompt.
    /// Helps the LLM understand which skills benefit from visualization.
    /// </summary>
    public string GetLearnedPatternsContext()
    {
        lock (_lock)
        {
            if (_pairings.Count == 0) return "";

            var topPairings = _pairings
                .OrderByDescending(p => p.Count)
                .Take(10)
                .Select(p =>
                {
                    _effectiveness.TryGetValue(p.PrecedingSkill, out var eff);
                    var effStr = eff != null ? $" (effectiveness: {eff.EffectivenessScore:F1})" : "";
                    return $"  - {p.PrecedingSkill} → {p.VisualizationAction} " +
                           $"[{p.MostFrequentSeverity}] ({p.Count}x, avg {p.TotalElements / Math.Max(1, p.Count)} elements){effStr}";
                });

            return "[learned_visualization_patterns]\n" +
                   "Skills that benefit from 3D visualization (auto-learned):\n" +
                   string.Join("\n", topPairings);
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        List<VisualSkillPairing> pairingsSnapshot;
        Dictionary<string, VisualizationEffectiveness> effSnapshot;

        lock (_lock)
        {
            pairingsSnapshot = [.. _pairings];
            effSnapshot = new Dictionary<string, VisualizationEffectiveness>(_effectiveness);
        }

        var data = new
        {
            pairings = pairingsSnapshot,
            effectiveness = effSnapshot.Values.ToList(),
            savedAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_dataPath, json, ct);
    }

    public Task LoadAsync(CancellationToken ct = default)
    {
        Load();
        return Task.CompletedTask;
    }

    private void Load()
    {
        if (!File.Exists(_dataPath)) return;

        try
        {
            var json = File.ReadAllText(_dataPath);
            var node = JsonNode.Parse(json);
            if (node is null) return;

            var pairingsNode = node["pairings"]?.AsArray();
            if (pairingsNode is not null)
            {
                foreach (var p in pairingsNode)
                {
                    if (p is null) continue;
                    _pairings.Add(new VisualSkillPairing
                    {
                        Key = p["Key"]?.GetValue<string>() ?? "",
                        PrecedingSkill = p["PrecedingSkill"]?.GetValue<string>() ?? "",
                        VisualizationAction = p["VisualizationAction"]?.GetValue<string>() ?? "",
                        SeveritiesUsed = p["SeveritiesUsed"]?.AsArray()
                            .Select(s => s?.GetValue<string>() ?? "").ToList() ?? [],
                        Count = p["Count"]?.GetValue<int>() ?? 0,
                        TotalElements = p["TotalElements"]?.GetValue<int>() ?? 0,
                        QuickClearCount = p["QuickClearCount"]?.GetValue<int>() ?? 0,
                        LastUsed = DateTime.TryParse(
                            p["LastUsed"]?.GetValue<string>(), out var dt) ? dt : DateTime.UtcNow
                    });
                }
            }

            var effNode = node["effectiveness"]?.AsArray();
            if (effNode is not null)
            {
                foreach (var e in effNode)
                {
                    if (e is null) continue;
                    var eff = new VisualizationEffectiveness
                    {
                        SkillName = e["SkillName"]?.GetValue<string>() ?? "",
                        PositiveCount = e["PositiveCount"]?.GetValue<int>() ?? 0,
                        NegativeCount = e["NegativeCount"]?.GetValue<int>() ?? 0,
                        NeutralCount = e["NeutralCount"]?.GetValue<int>() ?? 0
                    };
                    _effectiveness[eff.SkillName] = eff;
                }
            }
        }
        catch { /* corrupted data, start fresh */ }
    }
}

public class VisualSkillPairing
{
    public string Key { get; set; } = "";
    public string PrecedingSkill { get; set; } = "";
    public string VisualizationAction { get; set; } = "";
    public List<string> SeveritiesUsed { get; set; } = [];
    public int Count { get; set; }
    public int TotalElements { get; set; }
    public int QuickClearCount { get; set; }
    public DateTime LastUsed { get; set; }

    public string MostFrequentSeverity =>
        SeveritiesUsed.Count > 0 ? SeveritiesUsed[0] : "info";
}

public class VisualizationEffectiveness
{
    public string SkillName { get; set; } = "";
    public int PositiveCount { get; set; }
    public int NegativeCount { get; set; }
    public int NeutralCount { get; set; }

    public double EffectivenessScore
    {
        get
        {
            int total = PositiveCount + NegativeCount + NeutralCount;
            if (total == 0) return 0.5;
            return (PositiveCount + 0.3 * NeutralCount) / total;
        }
    }
}

public class VisualizationRecommendation
{
    public bool ShouldVisualize { get; set; }
    public string VisualizationAction { get; set; } = "";
    public string RecommendedSeverity { get; set; } = "info";
    public double Confidence { get; set; }
    public int AvgElementCount { get; set; }
    public string Reason { get; set; } = "";
}

public enum VisualizationFeedback
{
    Positive,
    Negative,
    Neutral
}
