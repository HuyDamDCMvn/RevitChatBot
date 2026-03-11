using RevitChatBot.Core.LLM;
using RevitChatBot.Core.Learning;

namespace RevitChatBot.Core.Skills;

/// <summary>
/// Captures user corrections and feeds them into the learning subsystem.
/// When the user says "no, that's wrong" or provides a better answer,
/// this skill records it into FewShotLearning, FailureRecoveryLearner,
/// and ImprovementStore for cross-session learning.
/// </summary>
[Skill("learn_from_correction",
    "Record a user correction to improve future responses. " +
    "Use when the user says something was wrong and provides the correct answer. " +
    "This teaches the bot to handle similar requests better next time.")]
[SkillParameter("original_query", "string",
    "The original user query that got the wrong response.",
    isRequired: true)]
[SkillParameter("correction_text", "string",
    "What the user said was wrong or what the correct answer should be.",
    isRequired: true)]
[SkillParameter("failed_skill", "string",
    "The skill that produced the wrong result (if known).",
    isRequired: false)]
[SkillParameter("correct_skill", "string",
    "The skill that should have been used instead (if known).",
    isRequired: false)]
[SkillParameter("correct_parameters", "string",
    "JSON-like string of correct parameters for the right skill.",
    isRequired: false)]
public class LearnFromCorrectionSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var originalQuery = parameters.GetValueOrDefault("original_query")?.ToString() ?? "";
        var correctionText = parameters.GetValueOrDefault("correction_text")?.ToString() ?? "";
        var failedSkill = parameters.GetValueOrDefault("failed_skill")?.ToString();
        var correctSkill = parameters.GetValueOrDefault("correct_skill")?.ToString();
        var correctParamsStr = parameters.GetValueOrDefault("correct_parameters")?.ToString();

        if (string.IsNullOrWhiteSpace(originalQuery) || string.IsNullOrWhiteSpace(correctionText))
            return SkillResult.Fail("Both original_query and correction_text are required.");

        var learnings = new List<string>();

        var fewShot = context.Extra.GetValueOrDefault("few_shot_learning") as AdaptiveFewShotLearning;
        if (fewShot is not null && !string.IsNullOrWhiteSpace(correctSkill))
        {
            var correctParams = new Dictionary<string, object?> { ["_correction"] = correctionText };
            fewShot.RecordSuccess(originalQuery, correctSkill, correctParams);
            learnings.Add($"Recorded few-shot example: \"{originalQuery}\" → {correctSkill}");
        }

        var failureRecovery = context.Extra.GetValueOrDefault("failure_recovery") as FailureRecoveryLearner;
        if (failureRecovery is not null && !string.IsNullOrWhiteSpace(failedSkill))
        {
            failureRecovery.RecordFailure(failedSkill, null, $"User correction: {correctionText}");
            learnings.Add($"Recorded failure pattern for '{failedSkill}'");

            if (!string.IsNullOrWhiteSpace(correctSkill))
            {
                failureRecovery.RecordRecovery(failedSkill, correctSkill, correctionText, true);
                learnings.Add($"Recorded recovery path: {failedSkill} → {correctSkill}");
            }
        }

        var improvementStore = context.Extra.GetValueOrDefault("improvement_store") as ImprovementStore;
        if (improvementStore is not null)
        {
            improvementStore.RecordImprovement(
                originalQuery,
                $"User corrected: {correctionText}. " +
                (correctSkill is not null ? $"Correct skill: {correctSkill}." : ""),
                qualityDelta: 0.3);
            learnings.Add("Stored improvement for future prompt enhancement");
        }

        if (learnings.Count == 0)
            return SkillResult.Fail(
                "Learning modules not available in context. Correction noted but not persisted.",
                "Set few_shot_learning, failure_recovery, improvement_store in SkillContext.Extra");

        return SkillResult.Ok(
            $"Correction recorded ({learnings.Count} learning modules updated):\n" +
            string.Join("\n", learnings.Select(l => $"  • {l}")),
            new { learningsApplied = learnings.Count, originalQuery, correctionText });
    }
}
