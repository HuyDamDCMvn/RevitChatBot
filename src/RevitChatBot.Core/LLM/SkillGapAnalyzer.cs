using System.Text.Json.Nodes;
using RevitChatBot.Core.Agent;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// Identifies skill gaps by finding queries that required codegen fallback.
/// A "gap" means the user asked for something that no built-in skill handles,
/// forcing the system to generate dynamic code. Frequent gaps are candidates
/// for new dedicated skills.
/// </summary>
public class SkillGapAnalyzer
{
    private readonly IOllamaService _ollama;
    private readonly SkillRegistry _registry;
    private readonly InteractionRecorder _recorder;

    public SkillGapAnalyzer(
        IOllamaService ollama,
        SkillRegistry registry,
        InteractionRecorder recorder)
    {
        _ollama = ollama;
        _registry = registry;
        _recorder = recorder;
    }

    /// <summary>
    /// Analyze recent interactions to find skill gaps.
    /// Returns a list of gaps ranked by frequency, with LLM-suggested skill definitions.
    /// </summary>
    public async Task<List<SkillGap>> AnalyzeGaps(
        int lookbackDays = 30, CancellationToken ct = default)
    {
        var fallbacks = _recorder.GetCodegenFallbacks(lookbackDays);
        if (fallbacks.Count == 0) return [];

        var gaps = fallbacks
            .GroupBy(r => r.Topic ?? "unknown")
            .Select(g => new SkillGap
            {
                Topic = g.Key,
                Frequency = g.Count(),
                ExampleQueries = g.Select(r => r.Query).Distinct().Take(5).ToList(),
                Priority = g.Count() >= 5 ? "high" : g.Count() >= 3 ? "medium" : "low"
            })
            .OrderByDescending(g => g.Frequency)
            .ToList();

        foreach (var gap in gaps.Where(g => g.Priority is "high" or "medium").Take(3))
        {
            try
            {
                gap.LlmSuggestion = await SuggestSkillDefinition(gap, ct);
            }
            catch { /* non-critical */ }
        }

        return gaps;
    }

    /// <summary>
    /// Get a summary of skill gaps for logging/reporting.
    /// </summary>
    public string GetGapSummary(List<SkillGap> gaps)
    {
        if (gaps.Count == 0) return "";
        var lines = new List<string> { "[skill_gaps]" };
        foreach (var g in gaps.Take(10))
            lines.Add($"  - {g.Topic}: {g.Frequency}x codegen fallback ({g.Priority}) " +
                       $"e.g. \"{g.ExampleQueries.FirstOrDefault()}\"");
        return string.Join("\n", lines);
    }

    private async Task<string?> SuggestSkillDefinition(SkillGap gap, CancellationToken ct)
    {
        var existingSkills = string.Join("\n", _registry.GetAllDescriptors()
            .Select(d => $"  - {d.Name}: {d.Description}"));

        var prompt = $"""
            Given these user requests that required dynamic code generation 
            (meaning no existing skill handled them):
            {string.Join("\n", gap.ExampleQueries.Select(q => $"  - \"{q}\""))}
            
            And these existing skills:
            {existingSkills}
            
            Suggest a new reusable skill that would handle these requests.
            Return the skill name (snake_case), description, and parameters.
            """;

        var result = await _ollama.GenerateAsync(prompt,
            formatJson: """
            {
                "type": "object",
                "properties": {
                    "name": {"type": "string"},
                    "description": {"type": "string"},
                    "parameters": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "name": {"type": "string"},
                                "type": {"type": "string"},
                                "description": {"type": "string"},
                                "required": {"type": "boolean"}
                            }
                        }
                    }
                },
                "required": ["name", "description"]
            }
            """,
            temperature: 0.3,
            numCtx: 4096,
            cancellationToken: ct);

        return result;
    }
}

public class SkillGap
{
    public string Topic { get; set; } = "";
    public int Frequency { get; set; }
    public List<string> ExampleQueries { get; set; } = [];
    public string Priority { get; set; } = "low";
    public string? LlmSuggestion { get; set; }
}
