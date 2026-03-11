using RevitChatBot.Core.Agent;
using RevitChatBot.Core.LLM;

namespace RevitChatBot.Core.Skills;

/// <summary>
/// Exposes the SkillGapAnalyzer as an invokable skill so the user (or agent)
/// can proactively discover what capabilities are missing.
/// </summary>
[Skill("analyze_skill_gaps",
    "Analyze what skills the bot is missing. Finds queries that required " +
    "code generation fallback (no built-in skill handled them) and suggests " +
    "new skills ranked by frequency and priority.")]
[SkillParameter("lookback_days", "integer",
    "Number of days to look back for interactions. Default 30.",
    isRequired: false)]
public class AnalyzeSkillGapsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var lookbackDays = 30;
        if (parameters.TryGetValue("lookback_days", out var lb) && lb is not null)
            int.TryParse(lb.ToString(), out lookbackDays);

        var analyzer = context.Extra.GetValueOrDefault("skill_gap_analyzer") as SkillGapAnalyzer;
        if (analyzer is null)
            return SkillResult.Fail("SkillGapAnalyzer not available. Set 'skill_gap_analyzer' in SkillContext.Extra.");

        var gaps = await analyzer.AnalyzeGaps(lookbackDays, cancellationToken);

        if (gaps.Count == 0)
            return SkillResult.Ok(
                "No skill gaps detected — all recent queries were handled by existing skills.",
                new { gapCount = 0 });

        var lines = new List<string> { $"Found {gaps.Count} skill gap(s) in the last {lookbackDays} days:\n" };
        foreach (var gap in gaps)
        {
            lines.Add($"**{gap.Topic}** (priority: {gap.Priority}, {gap.Frequency}x codegen fallback)");
            foreach (var q in gap.ExampleQueries.Take(3))
                lines.Add($"  - \"{q}\"");
            if (gap.LlmSuggestion is not null)
                lines.Add($"  💡 Suggested skill: {gap.LlmSuggestion}");
            lines.Add("");
        }

        return SkillResult.Ok(string.Join("\n", lines), new
        {
            gapCount = gaps.Count,
            highPriority = gaps.Count(g => g.Priority == "high"),
            gaps = gaps.Select(g => new { g.Topic, g.Frequency, g.Priority }).ToList()
        });
    }
}
