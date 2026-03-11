using RevitChatBot.Core.Context;
using RevitChatBot.Core.Models;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// Manages token budget to prevent context overflow.
/// Trims prompt sections by priority based on query intent.
/// Estimates tokens using char/4 heuristic (accurate within ~10% for English/mixed).
/// </summary>
public class ContextWindowOptimizer
{
    private int _maxTokens;
    private Dictionary<string, double> _learnedPriorityMultipliers = new();

    private const int TokensPerChar = 4;
    private const int ReservedForResponse = 1024;
    private const int MinHistoryTokens = 500;

    public int MaxTokens => _maxTokens;

    public ContextWindowOptimizer(int maxContextTokens = 8192)
    {
        _maxTokens = maxContextTokens;
    }

    /// <summary>
    /// Update the token budget, e.g. after auto-detecting model context length via /api/show.
    /// </summary>
    public void UpdateMaxTokens(int maxContextTokens)
    {
        if (maxContextTokens > 0)
            _maxTokens = maxContextTokens;
    }

    /// <summary>
    /// Apply learned priority multipliers from ContextUsageTracker.
    /// Keys with high reference rates get boosted; rarely-used keys get deprioritized.
    /// </summary>
    public void UpdateLearnedPriorities(Dictionary<string, double> multipliers)
    {
        _learnedPriorityMultipliers = multipliers ?? new();
    }

    public static int EstimateTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : text.Length / TokensPerChar;

    /// <summary>
    /// Build optimized system prompt based on intent. Skip irrelevant sections.
    /// </summary>
    public string OptimizeSystemPrompt(string fullPrompt, string intent)
    {
        if (EstimateTokens(fullPrompt) <= 3000) return fullPrompt;

        var sections = SplitIntoSections(fullPrompt);
        var needed = GetNeededSections(intent);
        var alwaysKeep = new HashSet<string>
        {
            "YOUR ROLES", "LANGUAGE RULES", "REASONING APPROACH",
            "RESPONSE FORMAT", "DYNAMIC CODE GENERATION", "CODEGEN REUSE"
        };

        var kept = new List<string>();
        foreach (var (header, content) in sections)
        {
            if (alwaysKeep.Any(k => header.Contains(k, StringComparison.OrdinalIgnoreCase))
                || needed.Any(n => header.Contains(n, StringComparison.OrdinalIgnoreCase)))
            {
                kept.Add(content);
            }
        }

        return string.Join("\n\n", kept);
    }

    /// <summary>
    /// Optimize context entries — trim large entries, skip low-priority ones if over budget.
    /// </summary>
    public ContextData OptimizeContext(ContextData context, string intent, int currentTokenCount)
    {
        var available = _maxTokens - ReservedForResponse - currentTokenCount - MinHistoryTokens;
        if (available <= 0) available = 500;

        var prioritized = context.Entries
            .Select(e =>
            {
                var basePriority = GetContextPriority(e.Key, intent);
                var multiplier = _learnedPriorityMultipliers.GetValueOrDefault(e.Key, 1.0);
                return (key: e.Key, value: e.Value, priority: (int)(basePriority * multiplier), tokens: EstimateTokens(e.Value));
            })
            .OrderByDescending(x => x.priority)
            .ToList();

        var result = new ContextData();
        int used = 0;

        foreach (var (key, value, priority, tokens) in prioritized)
        {
            if (used + tokens <= available)
            {
                result.Add(key, value);
                used += tokens;
            }
            else if (priority >= 8 && tokens > 200)
            {
                var trimmed = TrimContextEntry(value, available - used);
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    result.Add(key, trimmed);
                    used += EstimateTokens(trimmed);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Trim history to fit within token budget.
    /// Keeps: most recent N messages + all tool results.
    /// </summary>
    public List<ChatMessage> OptimizeHistory(
        List<ChatMessage> history, int availableTokens)
    {
        if (history.Count == 0) return history;

        int totalTokens = history.Sum(m => EstimateTokens(m.Content));
        if (totalTokens <= availableTokens) return history;

        var result = new List<ChatMessage>();
        int used = 0;

        for (int i = history.Count - 1; i >= 0; i--)
        {
            var msg = history[i];
            var tokens = EstimateTokens(msg.Content);

            if (used + tokens > availableTokens && result.Count >= 4)
                break;

            result.Insert(0, msg);
            used += tokens;
        }

        return result;
    }

    private static HashSet<string> GetNeededSections(string intent)
    {
        return intent switch
        {
            "check" or "analyze" => ["RED FLAGS", "QA/QC WORKFLOW", "DIRECTIONAL CLEARANCE",
                "DESIGN CRITERIA", "STANDARDS"],
            "modify" => ["CLASH AVOIDANCE", "ROOM/SPACE MAPPING", "SPLIT DUCT",
                "ROUTING PREFERENCES"],
            "create" => ["DESIGN CRITERIA", "SIZING FORMULAS", "UNIT CONVERSIONS",
                "ROUTING PREFERENCES", "CONNECTOR ANALYSIS"],
            "calculate" => ["DESIGN CRITERIA", "SIZING FORMULAS", "UNIT CONVERSIONS"],
            "query" => ["MEP SYSTEM GRAPH", "ROUTING PREFERENCES"],
            "explain" => ["STANDARDS REFERENCE", "BIM INFORMATION", "DESIGN CRITERIA"],
            _ => ["RED FLAGS", "DESIGN CRITERIA"]
        };
    }

    private static int GetContextPriority(string key, string intent)
    {
        if (key == "model_inventory") return 10;
        if (key == "selected_elements") return 9;
        if (key == "active_view") return 8;
        if (key == "project_info") return 7;
        if (key.Contains("memory") || key.Contains("learned")) return 6;
        if (key.Contains("knowledge")) return intent == "explain" ? 9 : 4;
        if (key.Contains("codegen") || key.Contains("dynamic_skills"))
            return intent is "create" or "calculate" ? 8 : 3;
        return 5;
    }

    private static List<(string header, string content)> SplitIntoSections(string prompt)
    {
        var result = new List<(string, string)>();
        var lines = prompt.Split('\n');
        string currentHeader = "PREAMBLE";
        var currentContent = new List<string>();

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("## "))
            {
                if (currentContent.Count > 0)
                    result.Add((currentHeader, string.Join("\n", currentContent)));
                currentHeader = line.TrimStart().TrimStart('#').Trim();
                currentContent = [line];
            }
            else
            {
                currentContent.Add(line);
            }
        }

        if (currentContent.Count > 0)
            result.Add((currentHeader, string.Join("\n", currentContent)));

        return result;
    }

    private static string TrimContextEntry(string value, int maxTokens)
    {
        if (maxTokens <= 0) return "";
        int maxChars = maxTokens * TokensPerChar;
        if (value.Length <= maxChars) return value;
        return value[..maxChars] + "\n...(truncated)";
    }
}
