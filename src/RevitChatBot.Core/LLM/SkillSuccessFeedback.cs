using RevitChatBot.Core.Memory;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// Boosts skill routing scores based on actual usage success analytics.
/// Skills that are frequently used and have high success rates get higher routing priority.
/// Integrates with SessionAnalytics and SemanticSkillRouter.
/// </summary>
public class SkillSuccessFeedback
{
    private readonly SessionAnalytics? _analytics;

    public SkillSuccessFeedback(SessionAnalytics? analytics)
    {
        _analytics = analytics;
    }

    /// <summary>
    /// Apply success-based boost to skill routing scores.
    /// Returns skills with adjusted ordering based on historical performance.
    /// </summary>
    public List<SkillDescriptor> ApplyFeedback(
        List<SkillDescriptor> routedSkills,
        QueryAnalysis analysis)
    {
        if (_analytics == null) return routedSkills;

        var stats = _analytics.GetSkillStats();
        if (stats.Count == 0) return routedSkills;

        var scored = routedSkills.Select((skill, originalIndex) =>
        {
            double boost = 0;

            if (stats.TryGetValue(skill.Name, out var stat))
            {
                double successRate = stat.TotalCalls > 0
                    ? (double)stat.SuccessCount / stat.TotalCalls
                    : 0;

                if (successRate >= 0.8 && stat.TotalCalls >= 3)
                    boost += 2.0;
                else if (successRate >= 0.5)
                    boost += 1.0;
                else if (successRate < 0.3 && stat.TotalCalls >= 5)
                    boost -= 1.0;

                if (stat.TotalCalls >= 10) boost += 0.5;

                if (stat.AvgDurationMs > 0 && stat.AvgDurationMs < 500)
                    boost += 0.3;
            }

            return (skill, score: routedSkills.Count - originalIndex + boost);
        })
        .OrderByDescending(x => x.score)
        .Select(x => x.skill)
        .ToList();

        return scored;
    }
}
