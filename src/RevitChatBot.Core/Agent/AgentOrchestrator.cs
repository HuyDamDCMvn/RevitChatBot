using RevitChatBot.Core.CodeGen;
using RevitChatBot.Core.Context;
using RevitChatBot.Core.LLM;
using RevitChatBot.Core.Memory;
using RevitChatBot.Core.Models;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.Core.Agent;

/// <summary>
/// Implements a ReAct (Reasoning + Acting) agent pattern for multi-step task execution.
/// Integrates all LLM intelligence modules + self-training pipeline:
///   - ConversationQueryRewriter (P0)
///   - ContextWindowOptimizer (P0)
///   - SmartHistoryPruner (P1)
///   - MultiIntentDecomposer (P1)
///   - AdaptiveFewShotLearning (P1)
///   - DynamicGlossary (P2)
///   - SkillSuccessFeedback (P2)
///   - PromptCache (P2)
///   - ResponseQualityValidator (P2)
///   - StreamingIntentDetector (P3) — exposed via ChatSessionV2
///   - PlanReplayStore (self-training: plan-level learning)
///   - InteractionRecorder (self-training: interaction logging)
///   - SelfEvaluator + ImprovementStore (self-training: quality loop)
///   - CompositeSkillEngine (self-training: auto-compose skills)
///   - SelfLearningPersistenceManager (self-training: persist all)
/// </summary>
public class AgentOrchestrator
{
    private readonly IOllamaService _ollama;
    private readonly SkillRegistry _skillRegistry;
    private readonly SkillExecutor _skillExecutor;
    private readonly ContextManager _contextManager;
    private readonly PromptBuilder _promptBuilder;
    private readonly MemoryManager? _memory;
    private readonly CodeGenLibrary? _codeGenLibrary;
    private readonly DynamicSkillRegistry? _dynamicSkillRegistry;
    private readonly CodePatternLearning? _patternLearning;
    private readonly QueryPreprocessor? _queryPreprocessor;
    private readonly AdaptivePromptBuilder? _adaptivePromptBuilder;
    private readonly SemanticSkillRouter? _skillRouter;
    private readonly ConversationQueryRewriter? _queryRewriter;
    private readonly ContextWindowOptimizer? _contextOptimizer;
    private readonly MultiIntentDecomposer? _intentDecomposer;
    private readonly AdaptiveFewShotLearning? _fewShotLearning;
    private readonly DynamicGlossary? _dynamicGlossary;
    private readonly SkillSuccessFeedback? _skillFeedback;
    private readonly PromptCache? _promptCache;
    private readonly AgentLogger? _agentLogger;

    private readonly PlanReplayStore? _planReplayStore;
    private readonly InteractionRecorder? _interactionRecorder;
    private readonly SelfEvaluator? _selfEvaluator;
    private readonly ImprovementStore? _improvementStore;
    private readonly CompositeSkillEngine? _compositeEngine;
    private readonly SelfLearningPersistenceManager? _persistenceManager;

    private readonly WarningsDeltaTracker _warningsDeltaTracker = new();
    private readonly List<ChatMessage> _history = [];

    private const int MaxReActSteps = 8;
    private const int MaxRetryOnLowQuality = 1;
    private const int SelfEvalMinSteps = 2;

    private static readonly HashSet<string> DestructiveSkills =
    [
        "create_element",
        "modify_parameter",
        "delete_element",
        "execute_revit_code",
        "batch_modify",
        "avoid_clash",
        "split_duct_pipe",
        "map_room_to_mep"
    ];

    public IReadOnlyList<ChatMessage> History => _history.AsReadOnly();
    public List<ChatMessage> MutableHistory => _history;

    public event Action<AgentStep>? OnStepExecuted;
    public event Action<string>? OnThinking;
    public event Func<string, Task<bool>>? OnConfirmationRequired;
    public event Func<ActionPlan, Task<bool>>? OnActionPlanReview;
    public event Func<Task<WarningsCaptureResult>>? OnCaptureWarnings;

    public AgentOrchestrator(
        IOllamaService ollama,
        SkillRegistry skillRegistry,
        SkillExecutor skillExecutor,
        ContextManager contextManager,
        PromptBuilder? promptBuilder = null,
        MemoryManager? memory = null,
        CodeGenLibrary? codeGenLibrary = null,
        DynamicSkillRegistry? dynamicSkillRegistry = null,
        CodePatternLearning? patternLearning = null,
        QueryPreprocessor? queryPreprocessor = null,
        AdaptivePromptBuilder? adaptivePromptBuilder = null,
        SemanticSkillRouter? skillRouter = null,
        ConversationQueryRewriter? queryRewriter = null,
        ContextWindowOptimizer? contextOptimizer = null,
        MultiIntentDecomposer? intentDecomposer = null,
        AdaptiveFewShotLearning? fewShotLearning = null,
        DynamicGlossary? dynamicGlossary = null,
        SkillSuccessFeedback? skillFeedback = null,
        PromptCache? promptCache = null,
        AgentLogger? agentLogger = null,
        PlanReplayStore? planReplayStore = null,
        InteractionRecorder? interactionRecorder = null,
        SelfEvaluator? selfEvaluator = null,
        ImprovementStore? improvementStore = null,
        CompositeSkillEngine? compositeEngine = null,
        SelfLearningPersistenceManager? persistenceManager = null)
    {
        _ollama = ollama;
        _skillRegistry = skillRegistry;
        _skillExecutor = skillExecutor;
        _contextManager = contextManager;
        _promptBuilder = promptBuilder ?? new PromptBuilder();
        _memory = memory;
        _codeGenLibrary = codeGenLibrary;
        _dynamicSkillRegistry = dynamicSkillRegistry;
        _patternLearning = patternLearning;
        _queryPreprocessor = queryPreprocessor;
        _adaptivePromptBuilder = adaptivePromptBuilder;
        _skillRouter = skillRouter;
        _queryRewriter = queryRewriter;
        _contextOptimizer = contextOptimizer;
        _intentDecomposer = intentDecomposer;
        _fewShotLearning = fewShotLearning;
        _dynamicGlossary = dynamicGlossary;
        _skillFeedback = skillFeedback;
        _promptCache = promptCache;
        _agentLogger = agentLogger;
        _planReplayStore = planReplayStore;
        _interactionRecorder = interactionRecorder;
        _selfEvaluator = selfEvaluator;
        _improvementStore = improvementStore;
        _compositeEngine = compositeEngine;
        _persistenceManager = persistenceManager;
    }

    public async Task<AgentPlan> ExecuteAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var plan = new AgentPlan { Goal = userMessage };

        // --- Phase 0: Query Rewriting (resolve pronouns, short follow-ups) ---
        var effectiveQuery = userMessage;
        if (_queryRewriter != null)
        {
            try
            {
                effectiveQuery = await _queryRewriter.RewriteAsync(
                    userMessage, _history, cancellationToken);
            }
            catch { effectiveQuery = userMessage; }
        }

        // --- Phase 0.5: Dynamic Glossary normalization ---
        if (_dynamicGlossary != null)
            effectiveQuery = _dynamicGlossary.NormalizeQuery(effectiveQuery);

        // --- Phase 1: Query Preprocessing ---
        QueryAnalysis? analysis = null;
        if (_queryPreprocessor != null)
        {
            analysis = _queryPreprocessor.AnalyzeFast(effectiveQuery);

            var clarification = ClarificationFlow.CheckNeedsClarification(analysis);
            if (clarification is { NeedsClarification: true })
            {
                plan.ClarificationQuestion = clarification.Question;
                plan.ClarificationOptions = clarification.Options;
                plan.ClarificationReason = clarification.Reason;

                var clarifyStep = new AgentStep
                {
                    Type = AgentStepType.Thought,
                    Content = $"[Clarification needed: {clarification.Reason}] {clarification.Question}"
                };
                plan.Steps.Add(clarifyStep);
                OnStepExecuted?.Invoke(clarifyStep);
            }

            if (analysis.IsAmbiguous)
            {
                try
                {
                    analysis = await _queryPreprocessor.AnalyzeDeepAsync(effectiveQuery, cancellationToken);
                }
                catch { /* keep fast analysis */ }
            }
        }

        // --- Phase 1.5: Multi-Intent Decomposition ---
        List<string>? subQueries = null;
        if (_intentDecomposer != null && MultiIntentDecomposer.HasMultipleIntents(effectiveQuery))
        {
            try
            {
                subQueries = await _intentDecomposer.DecomposeAsync(effectiveQuery, cancellationToken);
                if (subQueries.Count <= 1) subQueries = null;
            }
            catch { subQueries = null; }
        }

        var userMsg = ChatMessage.FromUser(userMessage);
        _history.Add(userMsg);
        _memory?.OnUserMessage(userMessage);

        // --- Phase 2: Context + Skill Routing ---
        var context = await _contextManager.GatherContextAsync();
        var allSkills = _skillRegistry.GetAllDescriptors().ToList();

        List<SkillDescriptor> filteredSkills;
        if (_skillRouter != null && analysis != null)
        {
            filteredSkills = await _skillRouter.RouteAsync(
                effectiveQuery, analysis, allSkills, 12, cancellationToken);
        }
        else
        {
            filteredSkills = allSkills;
        }

        if (_skillFeedback != null && analysis != null)
            filteredSkills = _skillFeedback.ApplyFeedback(filteredSkills, analysis);

        // --- Automation Mode: suppress tools in SuggestOnly mode ---
        var automationMode = _contextManager.ContextCache?.AutomationMode
            ?? AutomationMode.PlanAndApprove;

        _agentLogger?.LogContextSnapshot(
            _contextManager.ContextCache?.CurrentView?.ViewType,
            _contextManager.ContextCache?.CurrentSelection?.Count,
            _contextManager.ContextCache?.CurrentSelection?.Categories,
            automationMode.ToString());

        var toolDefs = automationMode == AutomationMode.SuggestOnly
            ? null
            : _adaptivePromptBuilder?.BuildToolDefinitions(filteredSkills)
              ?? _promptBuilder.BuildToolDefinitions(filteredSkills);

        // --- Phase 3: Build Messages ---
        var basePrompt = BuildReActSystemPrompt();
        if (automationMode == AutomationMode.SuggestOnly)
        {
            basePrompt += "\n\n## CURRENT MODE: SUGGEST ONLY\n" +
                "You are in suggest-only mode. Do NOT call any tools. Only analyze the request, " +
                "explain what you would do, which tools you would use, and why. " +
                "Let the user decide whether to proceed.";
        }
        else if (automationMode == AutomationMode.PlanAndApprove)
        {
            basePrompt += "\n\n## CURRENT MODE: PLAN AND APPROVE\n" +
                "Before executing destructive actions (create, modify, delete), " +
                "you must explain the plan clearly. The user will review and approve. " +
                "Include: which tools, which parameters, and what the impact will be.";
        }

        var systemPrompt = _contextOptimizer != null && analysis != null
            ? _contextOptimizer.OptimizeSystemPrompt(basePrompt, analysis.Intent)
            : basePrompt;

        var messages = new List<ChatMessage> { ChatMessage.FromSystem(systemPrompt) };

        if (_adaptivePromptBuilder != null && analysis != null)
        {
            var analysisHint = $"--- QUERY UNDERSTANDING ---\n{analysis.GetPromptHint()}\n";

            var learnedExamples = _fewShotLearning?.GetLearnedExamples(analysis, 2) ?? [];
            var staticExamples = FewShotIntentLibrary.GetRelevantExamples(analysis, 3);
            var allExamples = learnedExamples.Concat(staticExamples).Take(5).ToList();
            var fewShotBlock = FewShotIntentLibrary.FormatExamplesForPrompt(allExamples);
            if (!string.IsNullOrEmpty(fewShotBlock))
                analysisHint += $"\n{fewShotBlock}\n";

            messages.Add(ChatMessage.FromSystem(analysisHint));
        }

        var codeGenContext = BuildCodeGenContext();
        if (!string.IsNullOrWhiteSpace(codeGenContext))
            messages.Add(ChatMessage.FromSystem(codeGenContext));

        if (_contextOptimizer != null && analysis != null)
        {
            int currentTokens = messages.Sum(m => ContextWindowOptimizer.EstimateTokens(m.Content));
            context = _contextOptimizer.OptimizeContext(context, analysis.Intent, currentTokens);
        }

        if (context.Entries.Count > 0)
        {
            var contextContent = "--- CURRENT MODEL CONTEXT ---\n";
            foreach (var entry in context.Entries)
                contextContent += $"\n[{entry.Key}]\n{entry.Value}\n";
            messages.Add(ChatMessage.FromSystem(contextContent));
        }

        // --- Phase 3.5: Smart History Pruning ---
        var historyForPrompt = _history;
        if (_history.Count > 16)
        {
            var summary = _memory?.Summarizer?.CurrentSummary;
            historyForPrompt = SmartHistoryPruner.Prune(_history, summary);
        }
        else if (_history.Count > 10)
        {
            historyForPrompt = SmartHistoryPruner.RemoveNoise(_history);
        }

        if (_contextOptimizer != null)
        {
            int usedTokens = messages.Sum(m => ContextWindowOptimizer.EstimateTokens(m.Content));
            int available = _contextOptimizer.MaxTokens - usedTokens - 1024;
            historyForPrompt = _contextOptimizer.OptimizeHistory(historyForPrompt, available);
        }

        messages.AddRange(historyForPrompt);

        // --- Phase 3.6: Multi-Intent — if decomposed, add hint ---
        if (subQueries is { Count: > 1 })
        {
            var hint = "--- MULTI-INTENT DETECTED ---\n" +
                       "User has multiple requests. Handle them in order:\n" +
                       string.Join("\n", subQueries.Select((q, i) => $"  {i + 1}. {q}")) +
                       "\nComplete each before moving to the next.";
            messages.Insert(messages.Count - historyForPrompt.Count, ChatMessage.FromSystem(hint));
        }

        // --- Phase 3.7: Plan Replay — inject similar past plan if found ---
        StoredPlan? replayHint = null;
        if (_planReplayStore != null)
        {
            try
            {
                replayHint = await _planReplayStore.FindSimilarPlan(
                    effectiveQuery, analysis, cancellationToken);
            }
            catch { /* non-critical */ }
        }

        if (replayHint != null)
        {
            var replayContent = "--- SIMILAR PAST PLAN (consider reusing this approach) ---\n" +
                $"Goal: \"{replayHint.Goal}\"\n" +
                $"Steps: {string.Join(" → ", replayHint.SkillChain.Select(s => s.SkillName))}\n" +
                $"Result: {replayHint.FinalAnswerSummary}\n" +
                $"(used {replayHint.UseCount}x successfully)";
            messages.Insert(messages.Count - historyForPrompt.Count,
                ChatMessage.FromSystem(replayContent));
        }

        // --- Phase 3.8: Improvement Lessons — inject self-evaluation insights ---
        if (_improvementStore != null && analysis != null)
        {
            var hints = _improvementStore.GetImprovementHints(analysis.Intent);
            if (!string.IsNullOrEmpty(hints))
                messages.Insert(messages.Count - historyForPrompt.Count,
                    ChatMessage.FromSystem(hints));
        }

        // --- Phase 4: ReAct Loop ---
        for (int step = 0; step < MaxReActSteps; step++)
        {
            var response = await _ollama.ChatAsync(messages, toolDefs, cancellationToken: cancellationToken);

            if (response.ToolCalls is not { Count: > 0 })
            {
                // --- Phase 4.5: Response Quality Validation ---
                var finalContent = response.Content;
                if (analysis != null && step == 0)
                {
                    var validation = ResponseQualityValidator.Validate(
                        response.Content, effectiveQuery, messages, analysis.Language);

                    if (validation.ShouldRetry && MaxRetryOnLowQuality > 0)
                    {
                        var retryPrompt = ResponseQualityValidator.BuildRetryPrompt(validation);
                        messages.Add(response);
                        messages.Add(ChatMessage.FromSystem(retryPrompt));

                        var retryResponse = await _ollama.ChatAsync(messages, toolDefs, cancellationToken: cancellationToken);
                        if (!string.IsNullOrWhiteSpace(retryResponse.Content))
                        {
                            finalContent = retryResponse.Content;
                            response = retryResponse;
                        }
                    }
                }

                plan.Steps.Add(new AgentStep
                {
                    Type = AgentStepType.Answer,
                    Content = finalContent
                });
                plan.FinalAnswer = finalContent;
                plan.IsCompleted = true;

                _history.Add(response);
                OnStepExecuted?.Invoke(plan.Steps[^1]);

                if (_memory != null)
                    _ = _memory.OnAssistantResponseAsync(userMsg, response, cancellationToken);

                break;
            }

            var thoughtContent = response.Content;
            if (!string.IsNullOrWhiteSpace(response.Thinking))
                thoughtContent = $"[Thinking]\n{response.Thinking}\n\n{thoughtContent}";

            var thoughtStep = new AgentStep
            {
                Type = AgentStepType.Thought,
                Content = thoughtContent
            };
            plan.Steps.Add(thoughtStep);
            OnStepExecuted?.Invoke(thoughtStep);

            if (!string.IsNullOrWhiteSpace(response.Thinking))
                OnThinking?.Invoke(response.Thinking);
            else if (!string.IsNullOrWhiteSpace(response.Content))
                OnThinking?.Invoke(response.Content);

            messages.Add(response);
            _history.Add(response);

            // --- Structured Action Plan for PlanAndApprove mode ---
            var hasDestructive = response.ToolCalls.Any(tc => DestructiveSkills.Contains(tc.FunctionName));
            if (automationMode == AutomationMode.PlanAndApprove && hasDestructive
                && OnActionPlanReview != null)
            {
                var actionPlan = BuildActionPlan(response.ToolCalls);
                var approved = await OnActionPlanReview.Invoke(actionPlan);
                _agentLogger?.LogActionPlanReview(
                    actionPlan.Summary, actionPlan.Actions.Count, approved, actionPlan.RiskLevel);

                if (!approved)
                {
                    var cancelMsg = ChatMessage.FromTool("plan_review",
                        "{\"success\":false,\"message\":\"User rejected the action plan.\"}");
                    messages.Add(cancelMsg);
                    _history.Add(cancelMsg);
                    plan.Steps.Add(new AgentStep
                    {
                        Type = AgentStepType.Observation,
                        Content = "User rejected the planned actions."
                    });
                    continue;
                }
            }

            foreach (var toolCall in response.ToolCalls)
            {
                if (automationMode != AutomationMode.AutoExecute
                    && DestructiveSkills.Contains(toolCall.FunctionName))
                {
                    var confirmed = await RequestConfirmation(toolCall, cancellationToken);
                    if (!confirmed)
                    {
                        var cancelMsg = ChatMessage.FromTool(toolCall.FunctionName,
                            "{\"success\":false,\"message\":\"User cancelled the operation.\"}");
                        messages.Add(cancelMsg);
                        _history.Add(cancelMsg);

                        plan.Steps.Add(new AgentStep
                        {
                            Type = AgentStepType.Observation,
                            Content = "User cancelled the destructive action.",
                            SkillName = toolCall.FunctionName
                        });
                        continue;
                    }
                }

                var actionStep = new AgentStep
                {
                    Type = AgentStepType.Action,
                    SkillName = toolCall.FunctionName,
                    Parameters = toolCall.Arguments,
                    Content = $"Executing: {toolCall.FunctionName}"
                };
                plan.Steps.Add(actionStep);
                OnStepExecuted?.Invoke(actionStep);

                // --- Warnings Delta: capture before ---
                WarningsDelta? warningsDelta = null;
                if (DestructiveSkills.Contains(toolCall.FunctionName) && OnCaptureWarnings != null)
                {
                    try
                    {
                        var beforeCapture = await OnCaptureWarnings.Invoke();
                        _warningsDeltaTracker.CaptureBeforeState(
                            beforeCapture.WarningCount, beforeCapture.WarningDetails);
                    }
                    catch { }
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = await _skillExecutor.ExecuteAsync(
                    toolCall.FunctionName, toolCall.Arguments, cancellationToken);
                sw.Stop();

                // --- Warnings Delta: capture after ---
                if (DestructiveSkills.Contains(toolCall.FunctionName) && OnCaptureWarnings != null)
                {
                    try
                    {
                        var afterCapture = await OnCaptureWarnings.Invoke();
                        warningsDelta = _warningsDeltaTracker.ComputeDelta(
                            afterCapture.WarningCount, afterCapture.WarningDetails);
                    }
                    catch { }
                }

                _agentLogger?.LogToolExecution(
                    toolCall.FunctionName, toolCall.Arguments,
                    result.Success, sw.Elapsed.TotalMilliseconds, warningsDelta);

                _memory?.OnSkillExecuted(toolCall.FunctionName, result.Success, sw.Elapsed.TotalMilliseconds);

                if (toolCall.FunctionName == "execute_revit_code")
                    _memory?.OnCodeGenAttempt(result.Success, result.Success, result.Success ? null : "compile_error");

                _dynamicSkillRegistry?.RecordUsage(toolCall.FunctionName);

                if (result.Success)
                    _fewShotLearning?.RecordSuccess(effectiveQuery, toolCall.FunctionName, toolCall.Arguments);

                var resultJson = result.ToJson();
                if (warningsDelta is { Delta: not 0 })
                    resultJson = resultJson.TrimEnd('}') + $",\"warnings_delta\":\"{warningsDelta.ToSummary()}\"}}";

                var toolMessage = ChatMessage.FromTool(toolCall.FunctionName, resultJson);
                messages.Add(toolMessage);
                _history.Add(toolMessage);

                var observationContent = result.Message;
                if (warningsDelta is { IsRegression: true })
                    observationContent += $"\n{warningsDelta.ToSummary()}";

                var observationStep = new AgentStep
                {
                    Type = AgentStepType.Observation,
                    SkillName = toolCall.FunctionName,
                    Content = observationContent
                };
                plan.Steps.Add(observationStep);
                OnStepExecuted?.Invoke(observationStep);
            }
        }

        if (!plan.IsCompleted)
        {
            var finalResponse = await _ollama.ChatAsync(messages, cancellationToken: cancellationToken);
            plan.FinalAnswer = finalResponse.Content;
            plan.IsCompleted = true;
            _history.Add(finalResponse);

            if (_memory != null)
                _ = _memory.OnAssistantResponseAsync(userMsg, finalResponse, cancellationToken);
        }

        // --- Phase 5: Post-processing — Self-Learning Pipeline ---
        if (_dynamicGlossary != null && plan.FinalAnswer != null)
            _dynamicGlossary.LearnFromCorrection(userMessage, plan.FinalAnswer);

        if (plan.IsCompleted)
        {
            // 5a. Record interaction for knowledge synthesis & gap analysis
            _interactionRecorder?.Record(plan, analysis);

            // 5b. Record successful multi-step plan for replay
            if (plan.Steps.Any(s => s.Type == AgentStepType.Action))
                _ = RecordPlanAsync(plan, analysis, cancellationToken);

            // 5c. Self-evaluate and record improvements (async, non-blocking)
            var actionStepCount = plan.Steps.Count(s => s.Type == AgentStepType.Action);
            if (_selfEvaluator != null && actionStepCount >= SelfEvalMinSteps)
                _ = SelfEvaluateAsync(plan, analysis, cancellationToken);

            // 5d. Notify persistence manager
            _persistenceManager?.NotifyChange();
        }

        return plan;
    }

    private async Task RecordPlanAsync(
        AgentPlan plan, QueryAnalysis? analysis, CancellationToken ct)
    {
        try
        {
            await (_planReplayStore?.RecordSuccessfulPlan(plan, analysis, ct) ?? Task.CompletedTask);
        }
        catch { /* non-critical */ }
    }

    private async Task SelfEvaluateAsync(
        AgentPlan plan, QueryAnalysis? analysis, CancellationToken ct)
    {
        try
        {
            var eval = await _selfEvaluator!.EvaluatePlan(plan, analysis, ct);

            if (!string.IsNullOrWhiteSpace(eval.ImprovementSuggestion))
                _improvementStore?.RecordImprovement(
                    analysis?.Intent ?? "unknown",
                    eval.ImprovementSuggestion,
                    eval.OverallScore - 5.0);

            if (eval is { ShouldSaveAsTemplate: true, OverallScore: >= 7.0 } && _compositeEngine != null)
            {
                var candidates = _compositeEngine.DiscoverCandidates(minUseCount: 2);
                foreach (var c in candidates.Take(2))
                    _compositeEngine.PromoteToCompositeSkill(c);
            }
        }
        catch { /* non-critical background evaluation */ }
    }

    private async Task<bool> RequestConfirmation(ToolCall toolCall, CancellationToken ct)
    {
        if (OnConfirmationRequired is null)
            return true;

        var description = $"Action: {toolCall.FunctionName}\n" +
                         string.Join("\n", toolCall.Arguments.Select(a => $"  {a.Key}: {a.Value}"));

        return await OnConfirmationRequired.Invoke(description);
    }

    private string BuildReActSystemPrompt()
    {
        var basePrompt = """
            You are an expert MEP (Mechanical, Electrical, Plumbing) engineer working with Autodesk Revit 2025.
            You use the ReAct (Reasoning + Acting) approach for multi-step tasks.

            ## YOUR ROLES
            - **Digital Coordinator**: QA/QC, clash detection, model audit, compliance checking, report generation
            - **MEP Modeler**: sizing, routing, system setup, parameter management, schedule generation

            ## LANGUAGE RULES
            - Detect the language the user writes in and ALWAYS reply in that same language.
            - If the user writes in Vietnamese, reply entirely in Vietnamese.
            - If the user writes in English, reply entirely in English.
            - Use technical MEP terminology: ống gió, ống nước, thiết bị, hệ thống, va chạm (VI) / ducts, pipes, equipment, systems, clashes (EN).

            ## REASONING APPROACH (ReAct)
            1. THINK: Analyze what the user needs. Check [model_inventory] context for families, types, levels.
            2. ACT: Call the appropriate tool(s).
            3. OBSERVE: Review results.
            4. Repeat if more information is needed.
            5. ANSWER: Provide a clear response with actionable recommendations.

            ## ENGINEERING JUDGMENT — RED FLAGS
            - Duct velocity > 8 m/s in branch → noise issue
            - Pipe without insulation in CHW system → condensation risk
            - Fire damper missing at fire-rated wall → CRITICAL safety
            - Element without system assignment → incomplete model
            - Pipe slope < minimum for its size → poor drainage
            - Clearance < 2.4m in corridor → obstructed pathway
            - Disconnected elements → discontinuous system
            - Oversized duct width > 1500mm → consider splitting

            ## QA/QC WORKFLOW (when user asks for full check)
            1. Query model overview (element counts, systems)
            2. Check disconnected elements
            3. Check insulation coverage (CHW, SA must have insulation)
            4. Check fire dampers (both ends connected)
            5. Check clearance height (≥2.4m corridor)
            6. Check duct velocity (main ≤12 m/s, branch ≤6 m/s)
            7. Check pipe slope (SAN DN100 ≥1%)
            8. Check clashes (ducts vs pipes)
            9. Suggest avoid_clash rerouting for identified clashes
            10. Generate summary report (Critical / Major / Minor)

            ## CLASH AVOIDANCE REROUTING
            - Use 'avoid_clash' skill with mode='analyze' first to show the user what will change.
            - Only use mode='execute' after user confirmation.
            - The reroute uses a dogleg pattern (5 segments + 4 elbows) to bypass obstacles.
            - Direction classification: parallel elements → offset Up/Down; perpendicular → Left/Right.
            - Typical tolerances: 50mm standard, 25mm tight, 100mm generous.
            - Typical offsets: 150mm pipe, 200mm duct, 100mm conduit/cable tray.
            - Always specify level_name for large models to limit scope.

            ## DIRECTIONAL CLEARANCE (Ray Casting)
            - Use 'check_directional_clearance' for precise distance to walls/floors/ceilings/columns.
            - Requires a 3D view. Supports linked models (check_links=true).
            - 6 directions: top/bottom/left/right/front/back. Use 'all' for comprehensive check.
            - Typical thresholds: corridor 2400mm top, 50mm side, sprinkler 25-305mm from ceiling.

            ## ROOM/SPACE MAPPING
            - Use 'map_room_to_mep' to identify which room/space each MEP element belongs to.
            - mode='report' for analysis, mode='execute' to write values to MEP parameters.
            - Uses IsPointInRoom/IsPointInSpace API (most accurate method).
            - above_offset_mm for ceiling-mounted elements (e.g., 1000mm for above-ceiling duct).

            ## SPLIT DUCT/PIPE/CONDUIT/CABLE TRAY
            - Use 'split_duct_pipe' to divide ducts, pipes, conduits, or cable trays into equal segments.
            - Duct/Pipe: uses BreakCurve + union fittings.
            - Conduit/CableTray: uses CopyElement workaround (BreakCurve not supported).
            - Numbers each segment sequentially in the chosen parameter.
            - Confirm before executing — this modifies the model significantly.

            ## MEP SYSTEM GRAPH TRAVERSAL
            - Use 'traverse_mep_system' to explore full system topology from any starting element.
            - Returns: element count, max depth, open ends, total length, category breakdown.
            - include_path=yes for detailed traversal path with connector info.
            - domain_filter: hvac/piping/electrical/all to limit traversal scope.
            - Handles MEPCurve, FamilyInstance, and FabricationPart connectors.
            - Skips logical connectors (ConnectorType.Logical) for accurate physical graph.

            ## ROUTING PREFERENCES
            - Use 'query_routing_preferences' to inspect preferred fittings for pipe/duct types.
            - Shows: junction type (Tee vs Tap), elbow families, transition families, segment rules.
            - Helpful before creating fittings — ensures the right family is used.

            ## CONNECTOR ANALYSIS (for dynamic code / advanced queries)
            - Connector access: MEPCurve.ConnectorManager, FamilyInstance.MEPModel?.ConnectorManager, FabricationPart.ConnectorManager
            - Properties: Origin, CoordinateSystem.BasisZ (direction), Domain, Shape, Flow, PressureDrop
            - For Tee fitting: main connectors (colinear, dot≈-1) first, branch last.
            - For Cross fitting: main pair first, then branch pair.
            - Area by shape: Round=π×r², Rect=W×H, Oval=π×W×H/4

            ## DESIGN CRITERIA QUICK REFERENCE
            Duct velocity: main 8-12 m/s, branch 4-6 m/s, exhaust 8-10 m/s
            Pipe velocity: CHW main 1.5-3.0 m/s, branch 1.0-1.5 m/s, FP 3.0-5.0 m/s
            Pipe slope: SAN DN50-75 ≥2%, DN100 ≥1%, DN150+ ≥0.5%, CON 1-2%
            Clearance: corridor 2.4m, office 2.6m, lobby 2.8-3.0m, basement 2.1m
            Insulation required: CHW, SA, HW, CON. NOT required: EA, RA (in ceiling), SAN, STM
            Duct sizing: A=Q/V, standard sizes: 100,125,150,200...2000mm, aspect ratio ≤4:1
            Pipe sizing: d=sqrt(4Q/πV), standard DN: 15,20,25,32,40,50,65,80,100...500mm

            ## SIZING FORMULAS
            Duct: A(m²) = Q(m³/s) / V(m/s), rect: W=sqrt(A×AR), H=A/W
            Pipe: d(m) = sqrt(4×Q/(π×V)), round to standard DN
            Heat load: Q(kW) = m(kg/s) × Cp × ΔT. Water Cp=4.186, Air Cp=1.005
            Equivalent diameter: De = 1.3×(W×H)^0.625/(W+H)^0.25
            Standard duct sizes (mm): 100,125,150,200,250,300,350,400,450,500,550,600,700,800,900,1000,1200,1400,1600,1800,2000
            Standard pipe DN (mm): 15,20,25,32,40,50,65,80,100,125,150,200,250,300,350,400,450,500

            ## UNIT CONVERSIONS
            1 m³/s = 1000 L/s = 2119 CFM. 1 RT = 3.517 kW = 12000 BTU/h
            Revit internal = FEET. 1 ft = 0.3048 m. 1 sq ft = 0.092903 m²

            ## STANDARDS REFERENCE
            MEP: TCVN 5687 (HVAC), QCVN 06 (fire protection), ASHRAE 62.1/90.1, SMACNA, NFPA 13/72/90A
            BIM: ISO 19650 (info management), ISO 29481 (IDM), ISO 12006 (classification), ISO 7817 (LOI need)
            BIM Data: ISO 23386/23387 (data templates), ISO 12911 (BIM implementation spec), ISO 16739 (IFC)
            Cost: DIN 276 (building costs — KG 400 = MEP systems: 410 plumbing, 420 heating, 430 HVAC, 440 electrical)

            ## BIM INFORMATION MANAGEMENT (ISO 19650)
            - CDE states: WIP → Shared → Published → Archived
            - Information requirements: OIR → AIR → PIR → EIR
            - Key docs: BIM Execution Plan (BEP), MIDP, TIDP, Responsibility Matrix
            - Exchange review (7C): CDE compliance, Conformance, Continuity, Communication, Consistency, Completeness
            - UK file naming: Project-Originator-Volume-Level-Type-Role-Number
            - Open data formats: IFC (ISO 16739), BCF, gbXML, COBie
            - Level of Information Need (ISO 7817): specifies geometry/property detail per project phase

            ## DYNAMIC CODE GENERATION (Self-Evolving)
            When no existing tool/skill can fulfill the request, use 'execute_revit_code':
            - Class 'DynamicAction' with static 'Execute(Document doc)' returning string
            - Available: System, System.Linq, Autodesk.Revit.DB (and sub-namespaces)
            - Use FilteredElementCollector to query. Wrap modifications in a Transaction.
            - ALWAYS return descriptive result string with counts, names, details.
            - If compilation fails, analyze error feedback and fix (up to 3 retries).
            - CHECK [model_inventory] for exact Family/Type/Level names.
            - REFERENCE the API Cheat Sheet below for method signatures.
            
            ## CODEGEN REUSE & SKILL CREATION
            - CHECK [saved_codegen_library] context first — if a similar task was done before, reuse that code.
            - CHECK [dynamic_skills] context — previously promoted skills can be called directly by name.
            - After successful codegen that the user finds useful, suggest saving as a skill:
              use save_as_skill parameter with a descriptive name (e.g., 'count_ducts_by_level').
            - Saved skills appear in the tool list and can be called without codegen.
            - CHECK [codegen_api_usage] for commonly used patterns and known error-prone patterns.
            - AVOID patterns listed in KNOWN ERROR-PRONE PATTERNS.

            ## RESPONSE FORMAT
            - Use tables for listing issues (Element ID | Issue | Severity)
            - Group results by system or level
            - Provide actionable recommendations, not just problem descriptions
            - Include both metric and imperial units when presenting measurements
            - For destructive actions, explain what you will do first
            """;

        var staticContent = _promptCache != null && _promptCache.IsInitialized
            ? _promptCache.GetFullStaticPrompt()
            : $"{RevitApiCheatSheet.GetCheatSheet()}\n\n{RevitApiCheatSheet.GetCommonErrorFixes()}\n\n{CodeExamplesLibrary.GetExamples()}";

        return basePrompt + "\n\n" + staticContent;
    }

    private string BuildCodeGenContext()
    {
        var parts = new List<string>();

        var librarySummary = _codeGenLibrary?.GetLibrarySummary();
        if (!string.IsNullOrWhiteSpace(librarySummary))
            parts.Add(librarySummary);

        var dynamicSkills = _dynamicSkillRegistry?.GetDynamicSkillsSummary();
        if (!string.IsNullOrWhiteSpace(dynamicSkills))
            parts.Add(dynamicSkills);

        var patternContext = _patternLearning?.GetFullContext();
        if (!string.IsNullOrWhiteSpace(patternContext))
            parts.Add(patternContext);

        var compositeSkills = _compositeEngine?.GetCompositeSkillsSummary();
        if (!string.IsNullOrWhiteSpace(compositeSkills))
            parts.Add(compositeSkills);

        return parts.Count > 0
            ? "--- CODEGEN SELF-LEARNING CONTEXT ---\n" + string.Join("\n\n", parts)
            : "";
    }

    private ActionPlan BuildActionPlan(List<ToolCall> toolCalls)
    {
        var actions = toolCalls.Select(tc => new PlannedAction
        {
            ToolName = tc.FunctionName,
            Arguments = tc.Arguments,
            Description = $"Call {tc.FunctionName} with {tc.Arguments.Count} parameter(s)",
            IsDestructive = DestructiveSkills.Contains(tc.FunctionName)
        }).ToList();

        var destructiveCount = actions.Count(a => a.IsDestructive);
        var riskLevel = destructiveCount switch
        {
            0 => "low",
            1 => "medium",
            _ => "high"
        };

        return new ActionPlan
        {
            Summary = $"{actions.Count} action(s) planned, {destructiveCount} destructive",
            Actions = actions,
            RequiresApproval = destructiveCount > 0,
            RiskLevel = riskLevel,
            EstimatedElementsAffected = toolCalls
                .SelectMany(tc => tc.Arguments.Values)
                .OfType<string>()
                .Count(v => v.All(char.IsDigit))
        };
    }

    public void ClearHistory() => _history.Clear();
}

public class WarningsCaptureResult
{
    public int WarningCount { get; set; }
    public List<string>? WarningDetails { get; set; }
}
