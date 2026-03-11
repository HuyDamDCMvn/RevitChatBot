using RevitChatBot.Core.Learning;

namespace RevitChatBot.Core.Skills;

/// <summary>
/// Exposes the CrossSkillCorrelator as an invokable skill.
/// Reveals relationships between skills: which skills are commonly
/// used together, in what order, and with what success rates.
/// </summary>
[Skill("cross_skill_correlator",
    "Analyze correlations between skills: which are commonly used together, " +
    "in what order, and their combined success rates. Useful for understanding " +
    "workflow patterns and optimizing skill chains.")]
[SkillParameter("skill_name", "string",
    "A specific skill to analyze correlations for. If omitted, shows top correlations globally.",
    isRequired: false)]
[SkillParameter("min_correlation", "number",
    "Minimum correlation score (0-1) to include. Default: 0.3.",
    isRequired: false)]
public class CrossSkillCorrelatorSkill : ISkill
{
    public Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var skillName = parameters.GetValueOrDefault("skill_name")?.ToString();
        var minCorr = 0.3;
        if (parameters.TryGetValue("min_correlation", out var mc) && mc is not null)
            double.TryParse(mc.ToString(), out minCorr);

        var correlator = context.Extra.GetValueOrDefault("cross_skill_correlator") as CrossSkillCorrelator;
        if (correlator is null)
            return Task.FromResult(SkillResult.Fail(
                "CrossSkillCorrelator not available. Set 'cross_skill_correlator' in SkillContext.Extra."));

        var correlations = correlator.GetTopCorrelations(15);
        var ranking = correlator.GetPerformanceRanking();
        var exclusions = correlator.GetMutuallyExclusiveSkills();

        if (correlations.Count == 0 && ranking.Count == 0)
            return Task.FromResult(SkillResult.Ok(
                "No skill correlations found yet. Use the chatbot more to build correlation data.",
                new { correlationCount = 0 }));

        var lines = new List<string>();

        if (correlations.Count > 0)
        {
            lines.Add("**Skill Correlations** (skills commonly used together):");
            foreach (var c in correlations.Where(c => c.CorrelationStrength >= minCorr))
            {
                var dir = c.OrderMatters ? $"{c.SkillA} → {c.SkillB}" : $"{c.SkillA} ↔ {c.SkillB}";
                lines.Add($"  • {dir} (strength: {c.CorrelationStrength:P0}, co-occurred {c.CoOccurrence}x)");
            }
            lines.Add("");
        }

        if (ranking.Count > 0)
        {
            lines.Add("**Performance Ranking** (top skills by reliability × usage):");
            foreach (var s in ranking.Take(10))
                lines.Add($"  • {s.Name}: {s.TotalUses} uses, {s.SuccessRate:P0} success rate");
            lines.Add("");
        }

        if (!string.IsNullOrWhiteSpace(skillName))
        {
            var fallback = correlator.GetBestFallback(skillName);
            if (fallback is not null)
                lines.Add($"**Best fallback for '{skillName}':** {fallback}");
        }

        if (exclusions.Count > 0)
        {
            lines.Add("**Mutually Exclusive Skills** (never used together):");
            foreach (var (a, b) in exclusions.Take(5))
                lines.Add($"  • {a} ↮ {b}");
        }

        return Task.FromResult(SkillResult.Ok(
            string.Join("\n", lines),
            new
            {
                correlationCount = correlations.Count,
                rankedSkills = ranking.Count,
                exclusionCount = exclusions.Count
            }));
    }
}
