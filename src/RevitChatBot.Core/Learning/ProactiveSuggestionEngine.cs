using RevitChatBot.Core.Agent;
using RevitChatBot.Core.CodeGen;
using RevitChatBot.Core.LLM;
using RevitChatBot.Core.Memory;

namespace RevitChatBot.Core.Learning;

/// <summary>
/// Synthesizes insights from ALL learning modules to generate actionable
/// proactive suggestions. These suggestions are injected into the agent's
/// prompt so it can proactively offer guidance without waiting to be asked.
///
/// Suggestion types and their signal sources:
///
///   WORKFLOW_HINT: "After running check_clearance, you usually run highlight_elements"
///     Sources: CrossSkillCorrelator + InteractionRecorder
///
///   FOLLOW_UP_PREDICTION: "User will likely ask about duct sizing next"
///     Sources: UserBehaviorTracker (intent transition probabilities)
///
///   RECURRING_ISSUE_ALERT: "Level 3 frequently has clearance violations in this project"
///     Sources: ProjectProfiler + ImprovementStore
///
///   SKILL_GAP_WARNING: "This type of query usually needs codegen — consider suggesting a skill"
///     Sources: InteractionRecorder (codegen fallback patterns)
///
///   OPTIMIZATION_TIP: "check_velocity is slow — run it last in multi-check workflows"
///     Sources: CrossSkillCorrelator (performance stats)
///
///   TRUST_CALIBRATION: "User frequently corrects results — be extra careful with accuracy"
///     Sources: UserBehaviorTracker (correction rate)
///
///   EXPERTISE_ADAPTATION: "User is an expert — skip basic explanations"
///     Sources: UserBehaviorTracker (expertise signals)
/// </summary>
public class ProactiveSuggestionEngine
{
    public List<ProactiveSuggestion> Generate(
        ProjectProfile projectProfile,
        UserProfile userProfile,
        List<SkillCorrelation> correlations,
        List<InteractionRecord> recentRecords,
        ImprovementStore? improvementStore,
        SessionAnalytics? analytics,
        AdaptiveFewShotLearning? fewShot = null,
        CodePatternLearning? codePatterns = null)
    {
        var suggestions = new List<ProactiveSuggestion>();

        GenerateWorkflowHints(suggestions, correlations);
        GenerateFollowUpPredictions(suggestions, userProfile);
        GenerateRecurringIssueAlerts(suggestions, projectProfile);
        GenerateCodegenGapWarnings(suggestions, recentRecords);
        GeneratePerformanceTips(suggestions, analytics);
        GenerateTrustCalibration(suggestions, userProfile);
        GenerateExpertiseAdaptation(suggestions, userProfile);
        GenerateImprovementReminders(suggestions, improvementStore);
        GenerateFewShotInsights(suggestions, fewShot);
        GenerateCodePatternInsights(suggestions, codePatterns);

        return suggestions
            .OrderByDescending(s => s.Confidence)
            .Take(10)
            .ToList();
    }

    private static void GenerateWorkflowHints(
        List<ProactiveSuggestion> suggestions, List<SkillCorrelation> correlations)
    {
        foreach (var corr in correlations.Where(c => c.OrderMatters && c.CoOccurrence >= 3))
        {
            suggestions.Add(new ProactiveSuggestion
            {
                Type = SuggestionType.WorkflowHint,
                Suggestion = $"After '{corr.SkillA}', consider running '{corr.SkillB}' " +
                    $"(paired {corr.CoOccurrence}x in past workflows)",
                Confidence = Math.Min(corr.CorrelationStrength * 1.5, 1.0),
                SourceModule = "CrossSkillCorrelator"
            });
        }
    }

    private static void GenerateFollowUpPredictions(
        List<ProactiveSuggestion> suggestions, UserProfile profile)
    {
        if (profile.LastIntent is null) return;
        var predicted = profile.PredictNextIntent(profile.LastIntent);
        if (predicted is null) return;

        var transitionKey = $"{profile.LastIntent}→{predicted}";
        var count = profile.IntentTransitions.GetValueOrDefault(transitionKey);
        if (count < 2) return;

        suggestions.Add(new ProactiveSuggestion
        {
            Type = SuggestionType.FollowUpPrediction,
            Suggestion = $"User often follows '{profile.LastIntent}' with '{predicted}' " +
                $"({count}x observed). Prepare relevant context.",
            Confidence = Math.Min(count / 10.0, 0.95),
            SourceModule = "UserBehaviorTracker"
        });
    }

    private static void GenerateRecurringIssueAlerts(
        List<ProactiveSuggestion> suggestions, ProjectProfile profile)
    {
        var topIssues = profile.IssueFrequency
            .OrderByDescending(kv => kv.Value)
            .Where(kv => kv.Value >= 3)
            .Take(3);

        foreach (var (issue, count) in topIssues)
        {
            suggestions.Add(new ProactiveSuggestion
            {
                Type = SuggestionType.RecurringIssueAlert,
                Suggestion = $"Recurring issue in this project: '{issue}' (seen {count}x). " +
                    "Consider proactively checking related elements.",
                Confidence = Math.Min(count / 8.0, 0.9),
                SourceModule = "ProjectProfiler"
            });
        }
    }

    private static void GenerateCodegenGapWarnings(
        List<ProactiveSuggestion> suggestions, List<InteractionRecord> records)
    {
        var codegenTopics = records
            .Where(r => r.SkillsUsed.Contains("execute_revit_code"))
            .GroupBy(r => r.Topic ?? "unknown")
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Count())
            .Take(2);

        foreach (var group in codegenTopics)
        {
            suggestions.Add(new ProactiveSuggestion
            {
                Type = SuggestionType.SkillGapWarning,
                Suggestion = $"Topic '{group.Key}' required codegen {group.Count()}x. " +
                    "This topic may benefit from a dedicated skill.",
                Confidence = Math.Min(group.Count() / 5.0, 0.85),
                SourceModule = "InteractionRecorder"
            });
        }
    }

    private static void GeneratePerformanceTips(
        List<ProactiveSuggestion> suggestions, SessionAnalytics? analytics)
    {
        if (analytics is null) return;

        var slowSkills = analytics.SkillStats.Values
            .Where(s => s.TotalCalls >= 3 && s.AvgDurationMs > 5000)
            .OrderByDescending(s => s.AvgDurationMs)
            .Take(2);

        foreach (var skill in slowSkills)
        {
            suggestions.Add(new ProactiveSuggestion
            {
                Type = SuggestionType.OptimizationTip,
                Suggestion = $"'{skill.SkillName}' is slow (avg {skill.AvgDurationMs:F0}ms). " +
                    "Run it last in multi-step workflows or pre-filter parameters.",
                Confidence = 0.7,
                SourceModule = "SessionAnalytics"
            });
        }

        var unreliableSkills = analytics.SkillStats.Values
            .Where(s => s.TotalCalls >= 3 && s.SuccessRate < 0.6)
            .Take(2);

        foreach (var skill in unreliableSkills)
        {
            suggestions.Add(new ProactiveSuggestion
            {
                Type = SuggestionType.OptimizationTip,
                Suggestion = $"'{skill.SkillName}' has low success rate ({skill.SuccessRate:P0}). " +
                    "Check parameter quality or consider alternatives.",
                Confidence = 0.75,
                SourceModule = "SessionAnalytics"
            });
        }
    }

    private static void GenerateTrustCalibration(
        List<ProactiveSuggestion> suggestions, UserProfile profile)
    {
        if (profile.InteractionCount < 5) return;

        if (profile.TrustScore < 0.4)
        {
            suggestions.Add(new ProactiveSuggestion
            {
                Type = SuggestionType.TrustCalibration,
                Suggestion = "User has corrected responses frequently. " +
                    "Double-check results and add caveats to uncertain answers.",
                Confidence = 0.9,
                SourceModule = "UserBehaviorTracker"
            });
        }
    }

    private static void GenerateExpertiseAdaptation(
        List<ProactiveSuggestion> suggestions, UserProfile profile)
    {
        if (profile.InteractionCount < 5) return;

        var level = profile.EstimatedExpertise;
        if (level == "unknown") return;

        var hint = level switch
        {
            "expert" => "User is experienced — skip basic explanations, focus on specifics and edge cases.",
            "beginner" => "User seems new — provide context, explain MEP terms, and offer step-by-step guidance.",
            "intermediate" => "User has moderate experience — balance detail with efficiency.",
            _ => null
        };

        if (hint is not null)
        {
            suggestions.Add(new ProactiveSuggestion
            {
                Type = SuggestionType.ExpertiseAdaptation,
                Suggestion = hint,
                Confidence = 0.7 + Math.Min(profile.InteractionCount / 50.0, 0.2),
                SourceModule = "UserBehaviorTracker"
            });
        }

        if (profile.PreferredDetailLevel is not null)
        {
            suggestions.Add(new ProactiveSuggestion
            {
                Type = SuggestionType.ExpertiseAdaptation,
                Suggestion = $"User prefers {profile.PreferredDetailLevel} responses.",
                Confidence = 0.65,
                SourceModule = "UserBehaviorTracker"
            });
        }
    }

    private static void GenerateImprovementReminders(
        List<ProactiveSuggestion> suggestions, ImprovementStore? store)
    {
        if (store is null || store.Count == 0) return;

        var hint = store.GetImprovementHints("*", maxHints: 2);
        if (string.IsNullOrWhiteSpace(hint)) return;

        suggestions.Add(new ProactiveSuggestion
        {
            Type = SuggestionType.WorkflowHint,
            Suggestion = "Past self-evaluation noted: " + hint.Replace("\n", " ").Trim(),
            Confidence = 0.6,
            SourceModule = "ImprovementStore"
        });
    }

    private static void GenerateFewShotInsights(
        List<ProactiveSuggestion> suggestions, AdaptiveFewShotLearning? fewShot)
    {
        if (fewShot is null || fewShot.Count < 5) return;

        suggestions.Add(new ProactiveSuggestion
        {
            Type = SuggestionType.WorkflowHint,
            Suggestion = $"Adaptive learning has {fewShot.Count} learned examples from successful skill calls. " +
                "These are automatically prioritized in few-shot prompts for better accuracy.",
            Confidence = 0.5,
            SourceModule = "AdaptiveFewShotLearning"
        });
    }

    private static void GenerateCodePatternInsights(
        List<ProactiveSuggestion> suggestions, CodePatternLearning? codePatterns)
    {
        if (codePatterns is null) return;

        var errorWarnings = codePatterns.GetFrequentErrorWarnings();
        if (!string.IsNullOrWhiteSpace(errorWarnings))
        {
            suggestions.Add(new ProactiveSuggestion
            {
                Type = SuggestionType.OptimizationTip,
                Suggestion = "Codegen has recurring error patterns. " +
                    "Consider reviewing error-prone patterns to improve code generation success rate.",
                Confidence = 0.65,
                SourceModule = "CodePatternLearning"
            });
        }
    }
}

public class ProactiveSuggestion
{
    public SuggestionType Type { get; set; }
    public string Suggestion { get; set; } = "";
    public double Confidence { get; set; }
    public string SourceModule { get; set; } = "";
}

public enum SuggestionType
{
    WorkflowHint,
    FollowUpPrediction,
    RecurringIssueAlert,
    SkillGapWarning,
    OptimizationTip,
    TrustCalibration,
    ExpertiseAdaptation
}
