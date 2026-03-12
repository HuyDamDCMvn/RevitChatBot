using RevitChatBot.Core.CodeGen;
using RevitChatBot.Core.Context;
using RevitChatBot.Core.Learning;
using RevitChatBot.Core.LLM;
using RevitChatBot.Core.Memory;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.Core.Agent;

/// <summary>
/// Aggregate records grouping AgentOrchestrator dependencies by domain.
/// Use these when constructing AgentOrchestrator or ChatSessionV2 to avoid 30+ constructor params.
/// 
/// Migration: replace individual constructor params with these records.
/// The existing constructor remains for backward compatibility; these records
/// can be adopted incrementally.
/// </summary>
public record LlmIntelligenceDeps
{
    public QueryPreprocessor? QueryPreprocessor { get; init; }
    public AdaptivePromptBuilder? AdaptivePromptBuilder { get; init; }
    public SemanticSkillRouter? SkillRouter { get; init; }
    public ConversationQueryRewriter? QueryRewriter { get; init; }
    public ContextWindowOptimizer? ContextOptimizer { get; init; }
    public MultiIntentDecomposer? IntentDecomposer { get; init; }
    public AdaptiveFewShotLearning? FewShotLearning { get; init; }
    public DynamicGlossary? DynamicGlossary { get; init; }
    public SkillSuccessFeedback? SkillFeedback { get; init; }
    public PromptCache? PromptCache { get; init; }
}

public record CodeGenDeps
{
    public CodeGenLibrary? Library { get; init; }
    public DynamicSkillRegistry? DynamicSkillRegistry { get; init; }
    public CodePatternLearning? PatternLearning { get; init; }
    public KnowledgeCodeTemplateStore? CodeTemplateStore { get; init; }
    public SkillKnowledgeIndex? SkillKnowledgeIndex { get; init; }
    public DynamicCodeExamplesLibrary? DynamicExamplesLibrary { get; init; }
    public CodeGenKnowledgeEnricher? CodeGenKnowledgeEnricher { get; init; }
    public SkillKnowledgeRecipeStore? RecipeStore { get; init; }
}

public record LearningDeps
{
    public LearningModuleHub? Hub { get; init; }
    public LearningCortex? LearningCortex { get; init; }
    public FailureRecoveryLearner? FailureRecovery { get; init; }
    public ContextUsageTracker? ContextUsageTracker { get; init; }
}

public record SelfTrainingDeps
{
    public PlanReplayStore? PlanReplayStore { get; init; }
    public InteractionRecorder? InteractionRecorder { get; init; }
    public SelfEvaluator? SelfEvaluator { get; init; }
    public ImprovementStore? ImprovementStore { get; init; }
    public CompositeSkillEngine? CompositeEngine { get; init; }
    public SelfLearningPersistenceManager? PersistenceManager { get; init; }
}
