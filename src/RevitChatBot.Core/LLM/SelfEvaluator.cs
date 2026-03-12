using System.Text.Json.Nodes;
using RevitChatBot.Core.Agent;
using RevitChatBot.Core.Learning;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// Uses the LLM to evaluate completed plan quality.
/// Scores completeness, efficiency, and accuracy.
/// Generates improvement suggestions that feed into ImprovementStore.
/// Publishes plan_evaluated events via LearningModuleHub for cross-module learning.
/// Only runs for multi-step plans (2+ skill calls) to avoid overhead.
/// </summary>
public class SelfEvaluator
{
    private readonly IOllamaService _ollama;
    private LearningModuleHub? _hub;

    public SelfEvaluator(IOllamaService ollama)
    {
        _ollama = ollama;
    }

    public void SetHub(LearningModuleHub hub) => _hub = hub;

    /// <summary>
    /// Evaluate a completed plan. Returns scores and improvement suggestions.
    /// Uses /api/generate with structured output for fast, cheap evaluation.
    /// </summary>
    public async Task<PlanEvaluation> EvaluatePlan(
        AgentPlan plan, QueryAnalysis? analysis, CancellationToken ct = default)
    {
        try
        {
            var stepsSummary = string.Join("\n", plan.Steps
                .Where(s => s.Type is AgentStepType.Action or AgentStepType.Observation)
                .Select(s => $"  [{s.Type}] {s.SkillName ?? ""}: {Truncate(s.Content, 120)}"));

            var prompt = $"""
                Evaluate this MEP assistant plan execution.
                
                User Goal: "{plan.Goal}"
                Intent: {analysis?.Intent ?? "unknown"}
                Steps:
                {stepsSummary}
                
                Final Answer (excerpt): {Truncate(plan.FinalAnswer ?? "", 250)}
                
                Rate each aspect 1-10:
                - completeness: Did it fully answer the question?
                - efficiency: Were skill calls optimal (no redundant steps)?
                - accuracy: Were the right skills chosen?
                
                Also suggest one specific improvement for next time.
                Set should_save_as_template=true if this workflow is reusable.
                """;

            var result = await _ollama.GenerateAsync(prompt,
                formatJson: """
                {
                    "type": "object",
                    "properties": {
                        "completeness": {"type": "integer"},
                        "efficiency": {"type": "integer"},
                        "accuracy": {"type": "integer"},
                        "improvement": {"type": "string"},
                        "better_sequence": {"type": "string"},
                        "should_save_as_template": {"type": "boolean"}
                    },
                    "required": ["completeness", "efficiency", "accuracy"]
                }
                """,
                temperature: 0.2,
                numCtx: 2048,
                cancellationToken: ct);

            var evaluation = ParseEvaluation(result);
            PublishEvaluation(evaluation, plan);
            return evaluation;
        }
        catch
        {
            return new PlanEvaluation { Completeness = 5, Efficiency = 5, Accuracy = 5 };
        }
    }

    private void PublishEvaluation(PlanEvaluation eval, AgentPlan plan)
    {
        try
        {
            var skillsUsed = plan.Steps
                .Where(s => s.Type == AgentStepType.Action && s.SkillName is not null)
                .Select(s => s.SkillName!)
                .ToList();

            _hub?.Publish(new LearningEvent("SelfEvaluator",
                LearningEventTypes.PlanEvaluated,
                new PlanEvaluatedData
                {
                    Goal = plan.Goal,
                    OverallScore = eval.OverallScore,
                    Completeness = eval.Completeness,
                    Efficiency = eval.Efficiency,
                    Accuracy = eval.Accuracy,
                    ImprovementSuggestion = eval.ImprovementSuggestion,
                    BetterSkillSequence = eval.BetterSkillSequence,
                    ShouldSaveAsTemplate = eval.ShouldSaveAsTemplate,
                    SkillsUsed = skillsUsed
                }));
        }
        catch { /* non-critical */ }
    }

    private static PlanEvaluation ParseEvaluation(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node == null) return DefaultEval();

            return new PlanEvaluation
            {
                Completeness = Clamp(node["completeness"]?.GetValue<int>() ?? 5),
                Efficiency = Clamp(node["efficiency"]?.GetValue<int>() ?? 5),
                Accuracy = Clamp(node["accuracy"]?.GetValue<int>() ?? 5),
                ImprovementSuggestion = node["improvement"]?.GetValue<string>(),
                BetterSkillSequence = node["better_sequence"]?.GetValue<string>(),
                ShouldSaveAsTemplate = node["should_save_as_template"]?.GetValue<bool>() ?? false
            };
        }
        catch
        {
            return DefaultEval();
        }
    }

    private static PlanEvaluation DefaultEval() =>
        new() { Completeness = 5, Efficiency = 5, Accuracy = 5 };

    private static int Clamp(int value) => Math.Max(1, Math.Min(10, value));

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}

public class PlanEvaluation
{
    public int Completeness { get; set; }
    public int Efficiency { get; set; }
    public int Accuracy { get; set; }
    public double OverallScore => (Completeness + Efficiency + Accuracy) / 3.0;
    public string? ImprovementSuggestion { get; set; }
    public string? BetterSkillSequence { get; set; }
    public bool ShouldSaveAsTemplate { get; set; }
}
