using RevitChatBot.Core.LLM;

namespace RevitChatBot.Core.Skills;

/// <summary>
/// Proactive skill: analyzes the model context and recent interactions to
/// suggest optimizations the user may not have thought to ask for.
/// Uses the LLM to reason about potential improvements.
/// </summary>
[Skill("suggest_optimization",
    "Proactively suggest model or workflow optimizations. Analyzes recent interactions, " +
    "known failures, and skill gap data to recommend next steps and improvements.")]
[SkillParameter("focus_area", "string",
    "Optional area to focus on: 'performance', 'quality', 'compliance', 'workflow'. Default: auto-detect.",
    isRequired: false)]
[SkillParameter("model_summary", "string",
    "Brief summary of the current model state (e.g. 'HVAC model, 3 floors, 200 ducts'). " +
    "If omitted, general suggestions are provided.",
    isRequired: false)]
public class SuggestOptimizationSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var focusArea = parameters.GetValueOrDefault("focus_area")?.ToString() ?? "auto";
        var modelSummary = parameters.GetValueOrDefault("model_summary")?.ToString() ?? "";

        var ollama = context.Extra.GetValueOrDefault("ollama_service") as IOllamaService;
        if (ollama is null)
            return SkillResult.Fail("LLM service not available for generating suggestions.");

        var registry = context.Extra.GetValueOrDefault("skill_registry") as SkillRegistry;
        var failureRecovery = context.Extra.GetValueOrDefault("failure_recovery") as Learning.FailureRecoveryLearner;
        var gapAnalyzer = context.Extra.GetValueOrDefault("skill_gap_analyzer") as SkillGapAnalyzer;

        var contextParts = new List<string>();

        if (registry is not null)
        {
            var skills = registry.GetAllDescriptors()
                .Select(d => d.Name)
                .ToList();
            contextParts.Add($"Available skills ({skills.Count}): {string.Join(", ", skills.Take(30))}");
        }

        if (failureRecovery is not null)
        {
            var recoveryCtx = failureRecovery.BuildRecoveryContext(5);
            if (!string.IsNullOrWhiteSpace(recoveryCtx))
                contextParts.Add(recoveryCtx);
        }

        if (!string.IsNullOrWhiteSpace(modelSummary))
            contextParts.Add($"Model: {modelSummary}");

        var prompt = $"""
            You are an MEP engineering automation expert for Autodesk Revit.
            
            Context:
            {string.Join("\n", contextParts)}
            
            Focus area: {focusArea}
            
            Suggest 3-5 actionable optimizations the user should consider.
            For each suggestion:
            1. Title (short)
            2. Why it matters
            3. Which skill(s) to run (from available skills)
            4. Priority (high/medium/low)
            
            Be practical and specific to MEP engineering workflows.
            Return JSON array of suggestions.
            """;

        try
        {
            var result = await ollama.GenerateAsync(prompt,
                temperature: 0.4, numCtx: 4096, cancellationToken: cancellationToken);

            return SkillResult.Ok(
                $"Optimization suggestions generated:\n\n{result}",
                new { focusArea, rawResponse = result });
        }
        catch (Exception ex)
        {
            return SkillResult.Fail($"Failed to generate suggestions: {ex.Message}");
        }
    }
}
