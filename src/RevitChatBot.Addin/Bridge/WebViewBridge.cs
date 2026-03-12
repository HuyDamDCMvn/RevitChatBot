using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RevitChatBot.Addin.Commands;
using RevitChatBot.Addin.Handlers;
using RevitChatBot.Core.Agent;
using RevitChatBot.Core.CodeGen;
using RevitChatBot.Core.Context;
using RevitChatBot.Core.Learning;
using RevitChatBot.Core.LLM;
using RevitChatBot.Core.Memory;
using RevitChatBot.Core.Models;
using MemoryContextProvider = RevitChatBot.Core.Memory.MemoryContextProvider;
using RevitChatBot.Core.Skills;
using RevitChatBot.Knowledge.Embeddings;
using RevitChatBot.Knowledge.Search;
using RevitChatBot.Knowledge.Synthesis;
using RevitChatBot.Knowledge.VectorStore;
using RevitChatBot.MEP.Skills.Calculation;
using RevitChatBot.Visualization;
using RevitChatBot.Visualization.Context;
using RevitChatBot.Visualization.Learning;
using RevitChatBot.Visualization.Skills;

namespace RevitChatBot.Addin.Bridge;

public class WebViewBridge : IDisposable
{
    private readonly WebView2 _webView;
    private readonly RevitEventHandler _eventHandler;
    private readonly BridgeInitData _initData;
    private ChatSessionV2? _chatSession;
    private OllamaService? _ollamaService;
    private RevitContextCache _contextCache = new();
    private KnowledgeManager? _knowledgeManager;
    private KnowledgeContextProvider? _knowledgeContextProvider;
    private MemoryManager? _memoryManager;
    private AgentLogger? _agentLogger;
    private readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private CodeGenLibrary? _codeGenLibrary;
    private DynamicSkillRegistry? _dynamicSkillRegistry;
    private CodePatternLearning? _patternLearning;
    private DynamicCodeExecutor? _codeExecutor;
    private QueryPreprocessor? _queryPreprocessor;
    private AdaptivePromptBuilder? _adaptivePromptBuilder;
    private SemanticSkillRouter? _skillRouter;
    private ConversationQueryRewriter? _queryRewriter;
    private ContextWindowOptimizer? _contextOptimizer;
    private MultiIntentDecomposer? _intentDecomposer;
    private AdaptiveFewShotLearning? _fewShotLearning;
    private DynamicGlossary? _dynamicGlossary;
#pragma warning disable CS0649
    private SkillSuccessFeedback? _skillFeedback;
#pragma warning restore CS0649
    private PromptCache? _promptCache;

    private PlanReplayStore? _planReplayStore;
    private InteractionRecorder? _interactionRecorder;
    private SelfEvaluator? _selfEvaluator;
    private ImprovementStore? _improvementStore;
    private CompositeSkillEngine? _compositeEngine;
    private SelfLearningPersistenceManager? _persistenceManager;
    private SelfTrainingScheduler? _selfTrainingScheduler;
    private KnowledgeSynthesizer? _knowledgeSynthesizer;
    private LearningCortex? _learningCortex;
    private FailureRecoveryLearner? _failureRecovery;
    private VisualizationManager? _vizManager;
    private VisualFeedbackLearner? _vizLearner;
    private VisualWorkflowComposer? _vizComposer;
    private CalculationPreferenceStore? _calcPreferenceStore;

    private KnowledgeCodeTemplateStore? _codeTemplateStore;
    private SkillKnowledgeIndex? _skillKnowledgeIndex;
    private DynamicCodeExamplesLibrary? _dynamicExamplesLibrary;
    private CodeGenKnowledgeEnricher? _codeGenKnowledgeEnricher;
    private SkillKnowledgeRecipeStore? _recipeStore;
    private ContextUsageTracker? _contextUsageTracker;
    private LearningModuleHub? _learningHub;
    private SkillRegistry? _skillRegistry;
    private CodeAutoFixer? _codeAutoFixer;
    private OllamaCloudRouter? _cloudRouter;
    private CancellationTokenSource? _pullCts;
    private readonly List<IDisposable> _hubSubscriptions = [];

    private static readonly (string Name, string Description, string MinVram)[] RecommendedCodeGenModels =
    [
        ("qwen2.5-coder:32b-instruct-q4_K_M", "Best quality, 20GB+ VRAM", "20GB"),
        ("qwen2.5-coder:14b",                  "Great balance, 10GB+ VRAM", "10GB"),
        ("deepseek-coder-v2:16b",              "Strong reasoning, 12GB+ VRAM", "12GB"),
        ("qwen2.5-coder:7b",                   "Good for 8GB VRAM",  "6GB"),
        ("codellama:13b",                       "Solid, 10GB+ VRAM",  "10GB"),
        ("qwen2.5-coder:3b",                   "Lightweight, any GPU", "3GB"),
    ];

    public WebViewBridge(WebView2 webView, RevitEventHandler eventHandler, BridgeInitData initData)
    {
        _webView = webView;
        _eventHandler = eventHandler;
        _initData = initData;
    }

    public void Initialize()
    {
        _contextCache = _initData.ContextCache;

        _ollamaService = new OllamaService();
        _cloudRouter = new OllamaCloudRouter(_ollamaService);
        var skillRegistry = new SkillRegistry();
        _skillRegistry = skillRegistry;
        var skillContext = new SkillContext
        {
            RevitDocument = _initData.Document,
            RevitApiInvoker = async (action) =>
                await _eventHandler.ExecuteAsync(doc => action(doc)),
            RevitUIInvoker = async (action) =>
                await _eventHandler.ExecuteWithUIAsync(uiDoc => action(uiDoc)),
            Extra = { ["contextCache"] = _contextCache }
        };
        var skillExecutor = new SkillExecutor(skillRegistry, skillContext);
        var contextManager = new ContextManager();
        contextManager.SetRevitDocument(_initData.Document);
        contextManager.SetContextCache(_contextCache);
        contextManager.SetRevitApiInvoker(async action =>
            await _eventHandler.ExecuteAsync(doc => action(doc)));

        var addinDir = Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        _agentLogger = new AgentLogger(Path.Combine(addinDir, "logs"));
        contextManager.SetLogger(_agentLogger);

        RegisterMEPComponents(skillRegistry, contextManager);
        RegisterKnowledge(skillRegistry, contextManager);
        RegisterDynamicCode(skillRegistry);
        RegisterWebSkills(skillRegistry);
        RegisterVisionSkill(skillRegistry);
        InitializeQueryUnderstanding(skillRegistry);
        InitializeLLMIntelligence();
        InitializeLearningCortex();
        RegisterVisualization(skillRegistry, contextManager);
        InitializeCalculationStore(skillContext);

        if (_vizManager is not null)
            skillContext.VisualizationManager = _vizManager;

        skillContext.SendToUI = (type, content, data) =>
            SendToUI(new BridgeMessage { Type = type, Content = content, Data = data });

        skillContext.Extra["skill_descriptors"] = skillRegistry.GetAllDescriptors().ToList();

        _skillKnowledgeIndex?.IndexFromRegistry(skillRegistry);

        _memoryManager = InitializeMemory(_ollamaService, contextManager);
        if (_memoryManager != null)
            contextManager.Register(new MemoryContextProvider(_memoryManager));
        InitializeSelfTraining(skillRegistry, skillExecutor);

        WireCrossLearningSubscriptions();

        _chatSession = new ChatSessionV2(
            _ollamaService, skillRegistry, skillExecutor, contextManager,
            memory: _memoryManager,
            codeGenLibrary: _codeGenLibrary,
            dynamicSkillRegistry: _dynamicSkillRegistry,
            patternLearning: _patternLearning,
            queryPreprocessor: _queryPreprocessor,
            adaptivePromptBuilder: _adaptivePromptBuilder,
            skillRouter: _skillRouter,
            queryRewriter: _queryRewriter,
            contextOptimizer: _contextOptimizer,
            intentDecomposer: _intentDecomposer,
            fewShotLearning: _fewShotLearning,
            dynamicGlossary: _dynamicGlossary,
            skillFeedback: _skillFeedback,
            promptCache: _promptCache,
            agentLogger: _agentLogger,
            planReplayStore: _planReplayStore,
            interactionRecorder: _interactionRecorder,
            selfEvaluator: _selfEvaluator,
            improvementStore: _improvementStore,
            compositeEngine: _compositeEngine,
            persistenceManager: _persistenceManager,
            selfTrainingScheduler: _selfTrainingScheduler,
            learningCortex: _learningCortex,
            failureRecovery: _failureRecovery,
            codeTemplateStore: _codeTemplateStore,
            skillKnowledgeIndex: _skillKnowledgeIndex,
            dynamicExamplesLibrary: _dynamicExamplesLibrary,
            codeGenKnowledgeEnricher: _codeGenKnowledgeEnricher,
            recipeStore: _recipeStore,
            contextUsageTracker: _contextUsageTracker,
            hub: _learningHub);

        _chatSession.OnAgentStep += step =>
        {
            var stepType = step.Type switch
            {
                AgentStepType.Action => BridgeMessageTypes.SkillExecuting,
                AgentStepType.Observation => BridgeMessageTypes.SkillCompleted,
                AgentStepType.Thought => BridgeMessageTypes.AgentThinking,
                _ => BridgeMessageTypes.AgentStep
            };

            SendToUI(new BridgeMessage
            {
                Type = stepType,
                Content = step.Content,
                Data = new Dictionary<string, object?>
                {
                    ["stepType"] = step.Type.ToString(),
                    ["skillName"] = step.SkillName
                }
            });
        };

        _chatSession.OnStreamChunk += chunk =>
        {
            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.StreamChunk,
                Content = chunk
            });
        };

        _chatSession.OnConfirmationRequired += async description =>
        {
            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.ConfirmationRequired,
                Content = description
            });
            return true;
        };

        _chatSession.OnActionPlanReview += async actionPlan =>
        {
            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.ActionPlanReview,
                Content = actionPlan.Summary,
                Data = new Dictionary<string, object?>
                {
                    ["actions"] = actionPlan.Actions.Select(a => new
                    {
                        a.ToolName, a.Description, a.IsDestructive, a.Arguments
                    }).ToList(),
                    ["riskLevel"] = actionPlan.RiskLevel,
                    ["estimatedElementsAffected"] = actionPlan.EstimatedElementsAffected
                }
            });
            return true;
        };

        _chatSession.OnCaptureWarnings += async () =>
        {
            try
            {
                var result = await _eventHandler.ExecuteAsync(doc =>
                {
                    var warnings = doc.GetWarnings();
                    return new WarningsCaptureResult
                    {
                        WarningCount = warnings.Count,
                        WarningDetails = warnings
                            .Take(50)
                            .Select(w => w.GetDescriptionText())
                            .ToList()
                    };
                });
                return result as WarningsCaptureResult ?? new WarningsCaptureResult();
            }
            catch
            {
                return new WarningsCaptureResult();
            }
        };

        _contextCache.OnContextChanged += () =>
        {
            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.ContextSnapshot,
                Data = new Dictionary<string, object?>
                {
                    ["view"] = _contextCache.CurrentView?.ViewName,
                    ["viewType"] = _contextCache.CurrentView?.ViewType,
                    ["selectionCount"] = _contextCache.CurrentSelection?.Count,
                    ["categories"] = _contextCache.CurrentSelection?.Categories,
                    ["automationMode"] = _contextCache.AutomationMode.ToString()
                }
            });
        };

        WireVisualizationLearning();

        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _ = InitializeAllAsync();
    }

    /// <summary>
    /// Phased async initialization: groups modules by dependency order.
    /// Phase 1: Model + independent data stores (no cross-deps)
    /// Phase 2: Modules that depend on Phase 1 (codegen needs knowledge, training needs memory)
    /// Phase 3: Non-critical suggestion (CodeGen model check)
    /// </summary>
    private async Task InitializeAllAsync()
    {
        try
        {
            // Phase 1: Model detection + independent data stores (parallel)
            await Task.WhenAll(
                AutoDetectModelAsync(),
                InitializeKnowledgeAsync(),
                InitializeLLMModulesAsync(),
                InitializeLearningCortexAsync(),
                InitializeCalcStoreAsync());

            // Phase 2: Modules depending on Phase 1 (parallel)
            await Task.WhenAll(
                InitializeMemoryAsync(),
                InitializeCodeGenModulesAsync());

            // Phase 3: Depends on Phase 2 (memory + codegen ready)
            await InitializeSelfTrainingModulesAsync();

            // Phase 4: Non-critical background suggestion
            _ = CheckCodeGenModelAsync();
        }
        catch
        {
            // Initialization failures are non-fatal — individual init methods handle their own errors
        }
    }

    private void RegisterMEPComponents(
        SkillRegistry registry,
        ContextManager contextManager)
    {
        try
        {
            var mepAssembly = typeof(MEP.Skills.Query.QueryElementsSkill).Assembly;
            registry.RegisterFromAssembly(mepAssembly);
        }
        catch { }

        try
        {
            contextManager.Register(new MEP.Context.ProjectInfoProvider());
            contextManager.Register(new MEP.Context.ActiveViewProvider());

            var selectionProvider = new MEP.Context.SelectedElementsProvider
            {
                GetSelection = () =>
                {
                    var ids = _contextCache?.CurrentSelection?.ElementIds;
                    if (ids is null || ids.Count == 0)
                        return Array.Empty<Autodesk.Revit.DB.ElementId>();
                    return ids.Select(id => new Autodesk.Revit.DB.ElementId(id)).ToList();
                }
            };
            contextManager.Register(selectionProvider);

            contextManager.Register(new MEP.Context.MEPSystemProvider());
            contextManager.Register(new MEP.Context.RoomSpaceProvider());
            contextManager.Register(new MEP.Context.SystemDetailProvider());
            contextManager.Register(new MEP.Context.LevelSummaryProvider());
            contextManager.Register(new MEP.Context.ModelInventoryProvider());
        }
        catch { }
    }

    private void RegisterKnowledge(SkillRegistry registry, ContextManager contextManager)
    {
        try
        {
            var addinDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            var knowledgeDir = Path.Combine(addinDir, "knowledge");
            var indexPath = Path.Combine(addinDir, "knowledge_index.json");

            var embeddingService = new OllamaEmbeddingService();
            var vectorStore = new InMemoryVectorStore();
            _knowledgeManager = new KnowledgeManager(embeddingService, vectorStore, indexPath);

            _knowledgeContextProvider = new KnowledgeContextProvider(_knowledgeManager);
            contextManager.Register(_knowledgeContextProvider);

            var searchSkill = new KnowledgeSearchSkill(_knowledgeManager);
            registry.Register(searchSkill);
        }
        catch { }
    }

    private void RegisterDynamicCode(SkillRegistry registry)
    {
        try
        {
            var compiler = new RoslynCodeCompiler();
            compiler.Initialize();

            var revitApiPath = Path.GetDirectoryName(
                typeof(Autodesk.Revit.DB.Document).Assembly.Location);
            if (revitApiPath is not null)
            {
                compiler.AddReferenceFromFile(Path.Combine(revitApiPath, "RevitAPI.dll"));
                compiler.AddReferenceFromFile(Path.Combine(revitApiPath, "RevitAPIUI.dll"));
            }
            else
            {
                compiler.AddReferenceFromType(typeof(Autodesk.Revit.DB.Document));
            }

            compiler.AddReferenceFromType(typeof(RevitChatBot.RevitServices.FluentCollector));

            _codeExecutor = new DynamicCodeExecutor(compiler);

            var addinDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            var codegenDir = Path.Combine(addinDir, "codegen");
            Directory.CreateDirectory(codegenDir);

            _codeGenLibrary = new CodeGenLibrary(Path.Combine(codegenDir, "codegen_library.json"));
            _dynamicSkillRegistry = new DynamicSkillRegistry(
                Path.Combine(codegenDir, "dynamic_skills.json"), _codeExecutor, registry);
            _patternLearning = new CodePatternLearning(Path.Combine(codegenDir, "patterns.json"), _learningHub);

            _codeTemplateStore = new KnowledgeCodeTemplateStore();
            _dynamicExamplesLibrary = new DynamicCodeExamplesLibrary(
                Path.Combine(codegenDir, "learned_examples.json"));
            _codeGenKnowledgeEnricher = new CodeGenKnowledgeEnricher();
            _recipeStore = new SkillKnowledgeRecipeStore(
                Path.Combine(codegenDir, "recipes.json"));

            _skillKnowledgeIndex = new SkillKnowledgeIndex();
            _codeAutoFixer = new CodeAutoFixer(compiler);
            _codeExecutor.SetAutoFixer(_codeAutoFixer);

            var skill = new DynamicCodeSkill(
                _codeExecutor, _codeGenLibrary, _dynamicSkillRegistry, _patternLearning,
                _learningHub, _codeAutoFixer);
            registry.Register(skill);

            _learningHub?.Register("CodeGenLibrary", _codeGenLibrary);
            _learningHub?.Register("PatternLearning", _patternLearning);
            _learningHub?.Register("DynamicCodeExamples", _dynamicExamplesLibrary);
            _learningHub?.Register("RecipeStore", _recipeStore);
            _learningHub?.Register("SkillKnowledgeIndex", _skillKnowledgeIndex);
        }
        catch { }
    }

    private void RegisterWebSkills(SkillRegistry registry)
    {
        try
        {
            var opts = _ollamaService!.GetCurrentOptions();
            if (string.IsNullOrWhiteSpace(opts.CloudBaseUrl) ||
                string.IsNullOrWhiteSpace(opts.CloudApiKey))
                return;

            registry.Register(new OllamaWebSearchSkill(opts.CloudBaseUrl, opts.CloudApiKey));
            registry.Register(new OllamaWebFetchSkill(opts.CloudBaseUrl, opts.CloudApiKey));
        }
        catch { }
    }

    private void RegisterVisionSkill(SkillRegistry registry)
    {
        try
        {
            var visionSkill = new AnalyzeViewImageSkill(_ollamaService!);
            registry.Register(visionSkill);
        }
        catch { }
    }

    private void InitializeQueryUnderstanding(SkillRegistry skillRegistry)
    {
        try
        {
            var opts = _ollamaService!.GetCurrentOptions();
            _queryPreprocessor = new QueryPreprocessor(
                new HttpClient
                {
                    BaseAddress = new Uri(opts.BaseUrl),
                    Timeout = TimeSpan.FromSeconds(30)
                },
                opts.Model);

            _adaptivePromptBuilder = new AdaptivePromptBuilder();

            var embeddingAdapter = new OllamaEmbeddingAdapter(new OllamaEmbeddingService());
            _skillRouter = new SemanticSkillRouter(embeddingAdapter);

            _ = InitializeSkillRouterAsync(skillRegistry);
        }
        catch { }
    }

    private void InitializeLLMIntelligence()
    {
        try
        {
            var addinDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            var dataDir = Path.Combine(addinDir, "llm_data");
            Directory.CreateDirectory(dataDir);

            _queryRewriter = new ConversationQueryRewriter(_ollamaService!);

            var opts = _ollamaService!.GetCurrentOptions();
            _contextOptimizer = new ContextWindowOptimizer(opts.NumCtx ?? 8192);

            _intentDecomposer = new MultiIntentDecomposer(_ollamaService);

            _fewShotLearning = new AdaptiveFewShotLearning(
                Path.Combine(dataDir, "learned_fewshot.json"));

            _dynamicGlossary = new DynamicGlossary(
                Path.Combine(dataDir, "dynamic_glossary.json"));

            _promptCache = new PromptCache();
            _promptCache.Initialize();

            var embeddingAdapter = _skillRouter != null
                ? new OllamaEmbeddingAdapter(new OllamaEmbeddingService()) : null;

            _planReplayStore = new PlanReplayStore(
                Path.Combine(dataDir, "plan_replay.json"), embeddingAdapter);
            _interactionRecorder = new InteractionRecorder(
                Path.Combine(dataDir, "interactions.json"));
            _selfEvaluator = new SelfEvaluator(_ollamaService);
            _improvementStore = new ImprovementStore(
                Path.Combine(dataDir, "improvements.json"));
        }
        catch { }
    }

    private void InitializeLearningCortex()
    {
        try
        {
            var addinDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            var cortexDir = Path.Combine(addinDir, "learning_cortex");
            Directory.CreateDirectory(cortexDir);

            _learningHub = new LearningModuleHub();
            _failureRecovery = new FailureRecoveryLearner(cortexDir, _learningHub);
            _contextUsageTracker = new ContextUsageTracker(cortexDir);
            _learningCortex = new LearningCortex(cortexDir, _failureRecovery, _learningHub);

            _learningHub.Register("LearningCortex", _learningCortex);
            _learningHub.Register("FailureRecovery", _failureRecovery);
            _learningHub.Register("ContextUsageTracker", _contextUsageTracker);
        }
        catch { }
    }

    private void RegisterVisualization(SkillRegistry registry, ContextManager contextManager)
    {
        try
        {
            _vizManager = new VisualizationManager();

            registry.Register(new HighlightElementsSkill(_vizManager));
            registry.Register(new VisualizeClashSkill(_vizManager));
            registry.Register(new ClearVisualizationSkill(_vizManager));

            contextManager.Register(new VisualizationContextProvider(_vizManager));

            var addinDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            var vizDir = Path.Combine(addinDir, "viz_learning");
            Directory.CreateDirectory(vizDir);

            _vizLearner = new VisualFeedbackLearner(vizDir);
            _vizComposer = new VisualWorkflowComposer(_vizLearner, vizDir);
        }
        catch { }
    }

    private void InitializeCalculationStore(SkillContext skillContext)
    {
        try
        {
            var addinDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            var calcDataDir = Path.Combine(addinDir, "calc_data");
            Directory.CreateDirectory(calcDataDir);

            _calcPreferenceStore = new CalculationPreferenceStore(
                Path.Combine(calcDataDir, "calc_preferences.json"));
            skillContext.Extra["calc_preference_store"] = _calcPreferenceStore;
        }
        catch { }
    }

    /// <summary>
    /// Wire bidirectional hub subscriptions for ALL learning/codegen modules.
    /// Ensures every module both receives AND provides data through the event bus.
    /// </summary>
    private void WireCrossLearningSubscriptions()
    {
        if (_learningHub is null) return;

        void Track(IDisposable sub) => _hubSubscriptions.Add(sub);

        // ═══════════════════════════════════════════════════════
        // 1. REGISTER all modules not yet in hub
        // ═══════════════════════════════════════════════════════
        if (_dynamicGlossary is not null)
            _learningHub.Register("DynamicGlossary", _dynamicGlossary);
        if (_vizLearner is not null)
            _learningHub.Register("VisualFeedbackLearner", _vizLearner);
        if (_vizComposer is not null)
            _learningHub.Register("VisualWorkflowComposer", _vizComposer);
        if (_codeTemplateStore is not null)
            _learningHub.Register("KnowledgeCodeTemplateStore", _codeTemplateStore);
        if (_codeGenKnowledgeEnricher is not null)
            _learningHub.Register("CodeGenKnowledgeEnricher", _codeGenKnowledgeEnricher);
        if (_fewShotLearning is not null)
            _learningHub.Register("FewShotLearning", _fewShotLearning);
        if (_interactionRecorder is not null)
            _learningHub.Register("InteractionRecorder", _interactionRecorder);
        if (_improvementStore is not null)
            _learningHub.Register("ImprovementStore", _improvementStore);

        // ═══════════════════════════════════════════════════════
        // 2. skill_registered → reindex
        // ═══════════════════════════════════════════════════════
        if (_skillKnowledgeIndex is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.SkillRegistered], _ =>
            {
                try { _skillKnowledgeIndex.RebuildIndex(); }
                catch { /* non-critical */ }
            }));
        }

        // ═══════════════════════════════════════════════════════
        // 3. codegen_success / codegen_failure → cross-module learning
        // ═══════════════════════════════════════════════════════
        if (_patternLearning is not null)
        {
            Track(_learningHub.Subscribe(
                [LearningEventTypes.CodeGenSuccess, LearningEventTypes.CodeGenFailure], evt =>
            {
                var data = evt.GetData<CodeGenEventData>();
                if (data is null || evt.Source == "DynamicCodeSkill") return;
                if (evt.EventType == LearningEventTypes.CodeGenSuccess)
                    _patternLearning.RecordSuccess(data.Code, data.Query, data.ExecutionMs);
                else
                    _patternLearning.RecordFailure(data.Code, data.Error ?? "");
            }));
        }

        if (_codeGenLibrary is not null)
        {
            Track(_learningHub.Subscribe(
                [LearningEventTypes.CodeGenSuccess, LearningEventTypes.CodeGenFailure], evt =>
            {
                var data = evt.GetData<CodeGenEventData>();
                if (data is null || evt.Source == "DynamicCodeSkill") return;
                try
                {
                    if (evt.EventType == LearningEventTypes.CodeGenSuccess)
                        _codeGenLibrary.RecordSuccess(data.Query, data.Code, "", data.ExecutionMs);
                    else
                        _codeGenLibrary.RecordFailure(data.Query, data.Code, data.Error ?? "");
                }
                catch { /* non-critical */ }
            }));
        }

        if (_dynamicExamplesLibrary is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.CodeGenSuccess], evt =>
            {
                var data = evt.GetData<CodeGenEventData>();
                if (data is null) return;
                try
                {
                    _dynamicExamplesLibrary.LearnFromSuccess(
                        data.Code, data.Query, data.Query, null, 0);
                }
                catch { /* non-critical */ }
            }));
        }

        if (_dynamicGlossary is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.CodeGenSuccess], evt =>
            {
                var data = evt.GetData<CodeGenEventData>();
                if (data is null) return;
                try
                {
                    int added = 0;
                    foreach (var api in data.ApiPatternsUsed)
                    {
                        _dynamicGlossary.AddTerm(api, $"Revit API: {api}");
                        added++;
                    }
                    if (added > 0)
                    {
                        _learningHub.Publish(new LearningEvent(
                            "DynamicGlossary",
                            LearningEventTypes.GlossaryUpdated,
                            new { TermsAdded = added, Source = "codegen" }));
                    }
                }
                catch { /* non-critical */ }
            }));
        }

        if (_failureRecovery is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.CodeGenFailure], evt =>
            {
                var data = evt.GetData<CodeGenEventData>();
                if (data is null) return;
                try
                {
                    _failureRecovery.RecordFailure(
                        "execute_revit_code", null, data.Error ?? "codegen failure");
                }
                catch { /* non-critical */ }
            }));
        }

        if (_fewShotLearning is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.CodeGenSuccess], evt =>
            {
                var data = evt.GetData<CodeGenEventData>();
                if (data is null) return;
                try
                {
                    _fewShotLearning.RecordSuccess(
                        data.Query, "execute_revit_code",
                        new Dictionary<string, object?> { ["code"] = data.Code });
                }
                catch { /* non-critical */ }
            }));
        }

        // ═══════════════════════════════════════════════════════
        // 4. plan_completed → multi-module fan-out
        // ═══════════════════════════════════════════════════════
        if (_contextUsageTracker is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.PlanCompleted], evt =>
            {
                try
                {
                    var adjustments = _contextUsageTracker.GetPriorityAdjustments();
                    if (adjustments.Count > 0)
                    {
                        _learningHub.Publish(new LearningEvent(
                            "ContextUsageTracker",
                            LearningEventTypes.ContextUsed,
                            adjustments));
                    }
                }
                catch { /* non-critical */ }
            }));
        }

        if (_compositeEngine is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.PlanCompleted], evt =>
            {
                try
                {
                    var candidates = _compositeEngine.DiscoverCandidates(minUseCount: 3);
                    foreach (var c in candidates.Take(2))
                    {
                        if (_compositeEngine.PromoteToCompositeSkill(c))
                        {
                            _learningHub.Publish(new LearningEvent(
                                "CompositeSkillEngine",
                                LearningEventTypes.CompositeDiscovered,
                                new { c.SuggestedName, c.SuggestedDescription, c.UsageCount }));
                        }
                    }
                }
                catch { /* non-critical */ }
            }));
        }

        if (_recipeStore is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.PlanCompleted], evt =>
            {
                var data = evt.GetData<PlanCompletedData>();
                if (data is null || data.SkillsUsed.Count == 0) return;
                try
                {
                    var frequent = _recipeStore.DiscoverFrequentRecipes(minUseCount: 2);
                    if (frequent.Count > 0)
                        _ = _recipeStore.SaveAsync(CancellationToken.None);
                }
                catch { /* non-critical */ }
            }));
        }

        if (_improvementStore is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.PlanCompleted], evt =>
            {
                var data = evt.GetData<PlanCompletedData>();
                if (data is null) return;
                try
                {
                    _improvementStore.GetImprovementHints(data.Intent ?? "unknown");
                }
                catch { /* non-critical */ }
            }));
        }

        // ═══════════════════════════════════════════════════════
        // 5. composite_discovered → reindex + recipe sync
        // ═══════════════════════════════════════════════════════
        if (_skillKnowledgeIndex is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.CompositeDiscovered], _ =>
            {
                try { _skillKnowledgeIndex.RebuildIndex(); }
                catch { /* non-critical */ }
            }));
        }

        // ═══════════════════════════════════════════════════════
        // 6. skill_failure / skill_recovery → visual + correlator
        // ═══════════════════════════════════════════════════════
        if (_vizLearner is not null)
        {
            Track(_learningHub.Subscribe(
                [LearningEventTypes.SkillFailure, LearningEventTypes.SkillRecovery], evt =>
            {
                try
                {
                    if (evt.EventType == LearningEventTypes.SkillRecovery)
                    {
                        var data = evt.GetData<SkillRecoveryData>();
                        if (data != null)
                            _vizLearner.RecordEffectiveness(
                                data.RecoverySkill, VisualizationFeedback.Positive);
                    }
                }
                catch { /* non-critical */ }
            }));
        }

        // ═══════════════════════════════════════════════════════
        // 7. context_used → ContextWindowOptimizer
        // ═══════════════════════════════════════════════════════
        if (_contextOptimizer is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.ContextUsed], evt =>
            {
                var adjustments = evt.GetData<Dictionary<string, double>>();
                if (adjustments is not null)
                {
                    try { _contextOptimizer.UpdateLearnedPriorities(adjustments); }
                    catch { /* non-critical */ }
                }
            }));
        }

        // ═══════════════════════════════════════════════════════
        // 8. Sync learned fixes: CodePatternLearning → CodeAutoFixer
        // ═══════════════════════════════════════════════════════
        if (_codeAutoFixer is not null && _patternLearning is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.CodeGenSuccess], evt =>
            {
                if (evt.Source != "PatternLearning") return;
                try
                {
                    var fixes = _patternLearning.GetConfirmedFixes();
                    if (fixes.Count > 0)
                        _codeAutoFixer.UpdateLearnedFixes(fixes);
                }
                catch { /* non-critical */ }
            }));

            try
            {
                var existingFixes = _patternLearning.GetConfirmedFixes();
                if (existingFixes.Count > 0)
                    _codeAutoFixer.UpdateLearnedFixes(existingFixes);
            }
            catch { /* non-critical */ }
        }

        // ═══════════════════════════════════════════════════════
        // 9. plan_completed → InteractionRecorder (hub-driven record)
        // ═══════════════════════════════════════════════════════
        if (_interactionRecorder is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.PlanCompleted], evt =>
            {
                var data = evt.GetData<PlanCompletedData>();
                if (data is null) return;
                try
                {
                    _interactionRecorder.RecordFromEvent(data);
                }
                catch { /* non-critical */ }
            }));
        }

        // ═══════════════════════════════════════════════════════
        // 10. codegen_success → KnowledgeCodeTemplateStore (auto-extract templates)
        // ═══════════════════════════════════════════════════════
        if (_codeTemplateStore is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.CodeGenSuccess], evt =>
            {
                var data = evt.GetData<CodeGenEventData>();
                if (data is null || data.Code.Length < 50) return;
                try
                {
                    _codeTemplateStore.AddTemplate(
                        data.Query.Length > 60 ? data.Query[..60] : data.Query,
                        data.Code,
                        data.Intent,
                        data.Category);
                }
                catch { /* non-critical */ }
            }));
        }

        // ═══════════════════════════════════════════════════════
        // 11. plan_completed → VisualWorkflowComposer (discover visual patterns)
        // ═══════════════════════════════════════════════════════
        if (_vizComposer is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.PlanCompleted], evt =>
            {
                var data = evt.GetData<PlanCompletedData>();
                if (data is null || data.SkillsUsed.Count < 2) return;
                try
                {
                    _vizComposer.DiscoverVisualWorkflows();
                }
                catch { /* non-critical */ }
            }));
        }

        // ═══════════════════════════════════════════════════════
        // 12. Register PlanReplayStore + SelfEvaluator + Scheduler
        // ═══════════════════════════════════════════════════════
        if (_planReplayStore is not null)
            _learningHub.Register("PlanReplayStore", _planReplayStore);
        if (_selfEvaluator is not null)
        {
            _learningHub.Register("SelfEvaluator", _selfEvaluator);
            _selfEvaluator.SetHub(_learningHub);
        }
        if (_selfTrainingScheduler is not null)
            _learningHub.Register("SelfTrainingScheduler", _selfTrainingScheduler);

        // Wire DynamicGlossary hub for publish support
        _dynamicGlossary?.SetHub(_learningHub);

        // ═══════════════════════════════════════════════════════
        // 13. knowledge_indexed → CodeGenKnowledgeEnricher re-warm + CodeTemplateStore re-extract
        // ═══════════════════════════════════════════════════════
        if (_codeTemplateStore is not null || _codeGenKnowledgeEnricher is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.KnowledgeIndexed], _ =>
            {
                try
                {
                    _codeGenKnowledgeEnricher?.InvalidateCache();
                    _codeTemplateStore?.MarkStale();
                }
                catch { /* non-critical */ }
            }));
        }

        // ═══════════════════════════════════════════════════════
        // 14. codegen_success → RecipeStore (enrich with code patterns)
        // ═══════════════════════════════════════════════════════
        if (_recipeStore is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.CodeGenSuccess], evt =>
            {
                var data = evt.GetData<CodeGenEventData>();
                if (data is null) return;
                try
                {
                    _recipeStore.EnrichWithCodePattern(
                        data.Query, data.Code, data.Intent, data.Category);
                }
                catch { /* non-critical */ }
            }));
        }

        // ═══════════════════════════════════════════════════════
        // 15. skill_registered / composite_discovered → SemanticSkillRouter re-index
        // ═══════════════════════════════════════════════════════
        if (_skillRouter is not null)
        {
            Track(_learningHub.Subscribe(
                [LearningEventTypes.SkillRegistered, LearningEventTypes.CompositeDiscovered], evt =>
            {
                try
                {
                    if (_skillRegistry is not null)
                        _ = _skillRouter.InitializeAsync(_skillRegistry.GetAllDescriptors());
                }
                catch { /* non-critical */ }
            }));
        }

        // ═══════════════════════════════════════════════════════
        // 16. plan_evaluated → cross-module quality feedback
        // ═══════════════════════════════════════════════════════
        if (_fewShotLearning is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.PlanEvaluated], evt =>
            {
                var data = evt.GetData<PlanEvaluatedData>();
                if (data is null || data.OverallScore < 8.0) return;
                try
                {
                    foreach (var skill in data.SkillsUsed)
                        _fewShotLearning.RecordSuccess(data.Goal, skill,
                            new Dictionary<string, object?> { ["from_eval"] = true });
                }
                catch { /* non-critical */ }
            }));
        }

        if (_recipeStore is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.PlanEvaluated], evt =>
            {
                var data = evt.GetData<PlanEvaluatedData>();
                if (data is null || !data.ShouldSaveAsTemplate) return;
                try
                {
                    _recipeStore.DiscoverFrequentRecipes(minUseCount: 1);
                    _ = _recipeStore.SaveAsync(CancellationToken.None);
                }
                catch { /* non-critical */ }
            }));
        }

        // ═══════════════════════════════════════════════════════
        // 17. skill_gap_found → DynamicCodeExamplesLibrary prioritize learning
        // ═══════════════════════════════════════════════════════
        if (_dynamicExamplesLibrary is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.SkillGapFound], evt =>
            {
                var data = evt.GetData<SkillGapData>();
                if (data is null) return;
                try
                {
                    _dynamicExamplesLibrary.PrioritizeTopic(data.Topic, data.Frequency);
                }
                catch { /* non-critical */ }
            }));
        }

        // ═══════════════════════════════════════════════════════
        // 18. PromptCache auto-invalidation on learning updates
        // ═══════════════════════════════════════════════════════
        if (_promptCache is not null)
        {
            var sub = _promptCache.SubscribeToHub(_learningHub);
            if (sub is not null) Track(sub);
        }

        // ═══════════════════════════════════════════════════════
        // 19. glossary_updated → SkillKnowledgeIndex (new terms may affect routing)
        // ═══════════════════════════════════════════════════════
        if (_skillKnowledgeIndex is not null)
        {
            Track(_learningHub.Subscribe([LearningEventTypes.GlossaryUpdated], _ =>
            {
                try { _skillKnowledgeIndex.RebuildIndex(); }
                catch { /* non-critical */ }
            }));
        }
    }

    private void WireVisualizationLearning()
    {
        if (_chatSession is null) return;

        try
        {
            if (_vizManager is not null && _initData.Document is not null)
            {
                _ = _eventHandler.ExecuteAsync(doc =>
                {
                    var uiDoc = new Autodesk.Revit.UI.UIDocument(doc);
                    _vizManager.Register(doc, uiDoc);
                    return (object?)null;
                });
            }

            if (_vizLearner is not null)
            {
                _chatSession.Orchestrator.VisualFeedbackTracker =
                    (preceding, vizAction, args, success) =>
                    {
                        var severity = args?.GetValueOrDefault("severity")?.ToString() ?? "info";
                        _vizLearner.RecordSkillVisualizationPairing(
                            preceding, vizAction, severity, 0);
                    };
            }

            if (_vizLearner is not null && _vizComposer is not null)
            {
                var learnedPatterns =
                    (_vizLearner.GetLearnedPatternsContext() ?? "") + "\n" +
                    (_vizComposer.GetVisualWorkflowsContext() ?? "");

                if (!string.IsNullOrWhiteSpace(learnedPatterns))
                    _contextCache.Extra["visual_learned_patterns"] = learnedPatterns;
            }
        }
        catch { }
    }

    private async Task InitializeLearningCortexAsync()
    {
        try
        {
            var tasks = new List<Task>();
            if (_learningCortex is not null) tasks.Add(_learningCortex.LoadAsync());
            if (_failureRecovery is not null) tasks.Add(_failureRecovery.LoadAsync());
            if (_contextUsageTracker is not null) tasks.Add(_contextUsageTracker.LoadAsync());
            if (_vizLearner is not null) tasks.Add(_vizLearner.LoadAsync());
            if (_vizComposer is not null) tasks.Add(_vizComposer.LoadAsync());
            await Task.WhenAll(tasks);
        }
        catch { }
    }

    private async Task InitializeCalcStoreAsync()
    {
        try
        {
            if (_calcPreferenceStore is not null)
                await _calcPreferenceStore.LoadAsync();
        }
        catch { }
    }

    private void InitializeSelfTraining(SkillRegistry skillRegistry, SkillExecutor skillExecutor)
    {
        try
        {
            if (_planReplayStore != null)
                _compositeEngine = new CompositeSkillEngine(
                    _planReplayStore, skillRegistry, skillExecutor);

            _persistenceManager = new SelfLearningPersistenceManager(
                patternLearning: _patternLearning,
                dynamicSkills: _dynamicSkillRegistry,
                fewShotLearning: _fewShotLearning,
                glossary: _dynamicGlossary,
                codeGenLibrary: _codeGenLibrary,
                memory: _memoryManager,
                planStore: _planReplayStore,
                interactionRecorder: _interactionRecorder,
                improvementStore: _improvementStore,
                learningCortex: _learningCortex,
                failureRecovery: _failureRecovery,
                contextUsageTracker: _contextUsageTracker,
                recipeStore: _recipeStore,
                dynamicExamplesLibrary: _dynamicExamplesLibrary,
                codeTemplateStore: _codeTemplateStore,
                hub: _learningHub);

            var gapAnalyzer = _interactionRecorder != null
                ? new SkillGapAnalyzer(_ollamaService!, skillRegistry, _interactionRecorder)
                : null;

            var discoveryAgent = _planReplayStore != null
                ? new SkillDiscoveryAgent(_ollamaService!, skillRegistry, _planReplayStore)
                : null;

            _selfTrainingScheduler = new SelfTrainingScheduler(
                compositeEngine: _compositeEngine,
                recorder: _interactionRecorder,
                gapAnalyzer: gapAnalyzer,
                discoveryAgent: discoveryAgent,
                persistenceManager: _persistenceManager,
                logger: _agentLogger,
                recipeStore: _recipeStore,
                hub: _learningHub,
                skillKnowledgeIndex: _skillKnowledgeIndex);

            _learningHub?.Register("CompositeEngine", _compositeEngine!);
            _learningHub?.Register("PersistenceManager", _persistenceManager!);

            if (_knowledgeManager != null && _ollamaService != null)
            {
                var addinDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                var synthDir = Path.Combine(addinDir, "knowledge", "synthesized");
                _knowledgeSynthesizer = new KnowledgeSynthesizer(
                    _ollamaService, _knowledgeManager, synthDir);

                _selfTrainingScheduler.OnSynthesizeKnowledge = async (records, ct) =>
                    await _knowledgeSynthesizer.SynthesizeFromInteractions(records, ct);

                _selfTrainingScheduler.OnSetSynthesisPriorities = topics =>
                    _knowledgeSynthesizer.PrioritizedTopics = topics;
            }
        }
        catch { }
    }

    private async Task InitializeLLMModulesAsync()
    {
        try
        {
            var tasks = new List<Task>();
            if (_fewShotLearning != null) tasks.Add(_fewShotLearning.LoadAsync());
            if (_dynamicGlossary != null) tasks.Add(_dynamicGlossary.LoadAsync());
            await Task.WhenAll(tasks);

            await AutoDetectContextLengthAsync();
        }
        catch { }
    }

    /// <summary>
    /// Query model info via /api/show and update ContextWindowOptimizer with actual context length.
    /// </summary>
    private async Task AutoDetectContextLengthAsync()
    {
        if (_ollamaService is null || _contextOptimizer is null) return;
        try
        {
            var info = await _ollamaService.ShowModelAsync();
            if (info is { ContextLength: > 0 })
            {
                var running = await _ollamaService.ListRunningModelsAsync();
                var isGpu = running.Any(m => m.SizeVram > m.Size / 2);

                // Cap context to avoid slow CPU inference when no GPU offload
                const int maxCpuCtx = 8192;
                var effectiveCtx = isGpu
                    ? info.ContextLength
                    : Math.Min(info.ContextLength, maxCpuCtx);

                _contextOptimizer.UpdateMaxTokens(effectiveCtx);
                _ollamaService.UpdateOptions(opts =>
                {
                    if (opts.NumCtx is null || opts.NumCtx != effectiveCtx)
                        opts.NumCtx = effectiveCtx;
                });
            }
        }
        catch { }
    }

    private static readonly string[] PreferredModelPrefixes =
        ["qwen2.5", "qwen3", "llama3", "mistral", "gemma", "phi"];

    /// <summary>
    /// Detect installed Ollama models and auto-select the best one if no model is configured.
    /// Sends model_sync to UI so the frontend knows the active model and available list.
    /// </summary>
    private async Task AutoDetectModelAsync()
    {
        if (_ollamaService is null) return;
        try
        {
            var available = await _ollamaService.IsAvailableAsync();
            if (!available)
            {
                SendToUI(new BridgeMessage
                {
                    Type = BridgeMessageTypes.Error,
                    Content = "Cannot connect to Ollama at " +
                              _ollamaService.GetCurrentOptions().BaseUrl +
                              ". Make sure Ollama is running (ollama serve)."
                });
                return;
            }

            var models = await _ollamaService.ListModelsAsync();
            if (models.Count == 0)
            {
                SendToUI(new BridgeMessage
                {
                    Type = BridgeMessageTypes.Error,
                    Content = "No models installed in Ollama. Install one with: ollama pull qwen2.5:7b"
                });
                return;
            }

            var opts = _ollamaService.GetCurrentOptions();
            var currentModel = opts.Model;
            var modelExists = !string.IsNullOrEmpty(currentModel) &&
                              models.Any(m => m.Name.Equals(currentModel, StringComparison.OrdinalIgnoreCase));

            if (!modelExists)
            {
                var selected = PickBestModel(models);
                _ollamaService.UpdateOptions(o => o.Model = selected);
                currentModel = selected;
            }

            SendModelSync(currentModel, models);
        }
        catch { }
    }

    private static string PickBestModel(List<OllamaModel> models)
    {
        foreach (var prefix in PreferredModelPrefixes)
        {
            var match = models
                .Where(m => m.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.Size)
                .FirstOrDefault();
            if (match is not null) return match.Name;
        }
        return models.First().Name;
    }

    private void SendModelSync(string activeModel, List<OllamaModel> models)
    {
        SendToUI(new BridgeMessage
        {
            Type = BridgeMessageTypes.ModelSync,
            Content = activeModel,
            Data = new Dictionary<string, object?>
            {
                ["models"] = models.Select(m => new Dictionary<string, object?>
                {
                    ["name"] = m.Name,
                    ["parameterSize"] = m.ParameterSize,
                    ["quantization"] = m.QuantizationLevel,
                    ["sizeMB"] = m.Size / (1024 * 1024)
                }).ToList()
            }
        });
    }

    /// <summary>
    /// Adapter bridging Knowledge.Embeddings.IEmbeddingService -> Core.LLM.ISkillEmbeddingProvider.
    /// </summary>
    private class OllamaEmbeddingAdapter : ISkillEmbeddingProvider, IDisposable
    {
        private readonly OllamaEmbeddingService _inner;
        public OllamaEmbeddingAdapter(OllamaEmbeddingService inner) => _inner = inner;
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct)
            => _inner.GetEmbeddingAsync(text, ct);
        public Task<List<float[]>> GetEmbeddingsAsync(List<string> texts, CancellationToken ct)
            => _inner.GetEmbeddingsAsync(texts, ct);
        public void Dispose() => _inner.Dispose();
    }

    private async Task InitializeSkillRouterAsync(SkillRegistry skillRegistry)
    {
        try
        {
            if (_skillRouter != null)
                await _skillRouter.InitializeAsync(skillRegistry.GetAllDescriptors());
        }
        catch { }
    }

    private async Task InitializeKnowledgeAsync()
    {
        if (_knowledgeManager is null) return;
        try
        {
            await _knowledgeManager.LoadIndexAsync();

            var addinDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            var knowledgeDir = Path.Combine(addinDir, "knowledge");
            if (Directory.Exists(knowledgeDir))
                await _knowledgeManager.IndexDirectoryAsync(knowledgeDir);
        }
        catch { }
    }

    private MemoryManager? InitializeMemory(OllamaService ollama, ContextManager contextManager)
    {
        try
        {
            var addinDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            var memoryDir = Path.Combine(addinDir, "memory_data");
            var memory = new MemoryManager(memoryDir, ollama);
            return memory;
        }
        catch
        {
            return null;
        }
    }

    private async Task InitializeMemoryAsync()
    {
        if (_chatSession is null || _memoryManager is null) return;
        try
        {
            var projectTitle = _initData.DocumentTitle ?? "default";
            await _chatSession.InitializeMemoryAsync(projectTitle);

            var restored = _chatSession.History;
            if (restored.Count > 0)
            {
                SendToUI(new BridgeMessage
                {
                    Type = BridgeMessageTypes.AgentStep,
                    Content = $"Restored {restored.Count} messages from previous session.",
                    Data = new Dictionary<string, object?>
                    {
                        ["stepType"] = "MemoryRestore",
                        ["messageCount"] = restored.Count
                    }
                });
            }
        }
        catch { }
    }

    private async Task InitializeCodeGenModulesAsync()
    {
        try
        {
            var tasks = new List<Task>();
            if (_codeGenLibrary != null) tasks.Add(_codeGenLibrary.LoadAsync());
            if (_dynamicSkillRegistry != null) tasks.Add(_dynamicSkillRegistry.LoadAndRegisterAsync());
            if (_patternLearning != null) tasks.Add(_patternLearning.LoadAsync());
            if (_dynamicExamplesLibrary != null) tasks.Add(_dynamicExamplesLibrary.LoadAsync());
            if (_recipeStore != null) tasks.Add(_recipeStore.LoadAsync());
            await Task.WhenAll(tasks);

            if (_codeGenKnowledgeEnricher != null && _knowledgeManager != null)
            {
                _codeGenKnowledgeEnricher.SearchKnowledge = async (query, topK, ct) =>
                {
                    var results = await _knowledgeManager.SearchAsync(query, topK, ct);
                    return results.Select(r => (
                        Content: r.Entry.Text,
                        Source: r.Entry.Metadata.GetValueOrDefault("source", "unknown")
                    )).ToList();
                };
            }
        }
        catch { }
    }

    private async Task InitializeSelfTrainingModulesAsync()
    {
        try
        {
            var tasks = new List<Task>();
            if (_planReplayStore != null) tasks.Add(_planReplayStore.LoadAsync());
            if (_interactionRecorder != null) tasks.Add(_interactionRecorder.LoadAsync());
            if (_improvementStore != null) tasks.Add(_improvementStore.LoadAsync());
            await Task.WhenAll(tasks);

            _chatSession?.StartSelfTraining(TimeSpan.FromMinutes(30));
        }
        catch { }
    }

    private async void OnWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            if (json.StartsWith('"'))
                json = JsonSerializer.Deserialize<string>(json) ?? json;
            var message = JsonSerializer.Deserialize<BridgeMessage>(json, _jsonOpts);
            if (message is null) return;

            switch (message.Type)
            {
                case BridgeMessageTypes.UserMessage:
                    await HandleUserMessage(message.Content ?? "");
                    break;

                case BridgeMessageTypes.SettingsUpdate:
                    await HandleSettingsUpdateAsync(message.Data);
                    break;

                case "partial_input":
                    HandlePartialInput(message.Content ?? "");
                    break;

                case BridgeMessageTypes.HealthCheck:
                    await HandleHealthCheckAsync();
                    break;

                case BridgeMessageTypes.ModelInfo:
                    await HandleModelInfoAsync();
                    break;

                case BridgeMessageTypes.AutomationModeChanged:
                    HandleAutomationModeChange(message.Content ?? "");
                    break;

                case BridgeMessageTypes.MemoryConsent:
                    HandleMemoryConsent(message.Data);
                    break;

                case BridgeMessageTypes.MemoryStats:
                    HandleMemoryStatsRequest();
                    break;

                case BridgeMessageTypes.RequestSettings:
                    await HandleRequestSettingsAsync();
                    break;

                case BridgeMessageTypes.ModelPullRequest:
                    await HandleModelPullAsync(message.Data);
                    break;

                case BridgeMessageTypes.ModelPullCancel:
                    _pullCts?.Cancel();
                    break;

                case BridgeMessageTypes.CodeGenModelSet:
                    HandleCodeGenModelSet(message.Data);
                    break;
            }
        }
        catch (Exception ex)
        {
            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.Error,
                Content = ex.Message
            });
        }
    }

    private async Task HandleUserMessage(string content)
    {
        if (_chatSession is null || string.IsNullOrWhiteSpace(content)) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            _knowledgeContextProvider?.SetQuery(content);

            var response = await _chatSession.SendMessageAsync(content);
            sw.Stop();

            var lastPlan = _chatSession.LastPlan;
            if (lastPlan is { NeedsClarification: true })
            {
                SendToUI(new BridgeMessage
                {
                    Type = BridgeMessageTypes.ClarificationRequest,
                    Content = lastPlan.ClarificationQuestion,
                    Data = new Dictionary<string, object?>
                    {
                        ["options"] = lastPlan.ClarificationOptions,
                        ["reason"] = lastPlan.ClarificationReason
                    }
                });
            }

            var skillCount = lastPlan?.Steps
                .Count(s => s.Type == AgentStepType.Action) ?? 0;

            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.StreamEnd,
                Content = response,
                Data = new Dictionary<string, object?>
                {
                    ["durationMs"] = sw.ElapsedMilliseconds,
                    ["tokenEstimate"] = EstimateTokens(response),
                    ["skillsUsed"] = skillCount
                }
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.Error,
                Content = ex.Message
            });
        }
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)Math.Ceiling(text.Length / 3.5);
    }

    private void HandlePartialInput(string partialInput)
    {
        var partial = ChatSessionV2.AnalyzePartialInput(partialInput);
        if (partial != null && partial.Confidence >= 0.5)
        {
            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.PartialIntent,
                Content = partial.Intent,
                Data = new Dictionary<string, object?>
                {
                    ["category"] = partial.Category,
                    ["confidence"] = partial.Confidence
                }
            });
        }
    }

    private async Task HandleHealthCheckAsync()
    {
        if (_ollamaService is null) return;
        try
        {
            var available = await _ollamaService.IsAvailableAsync();
            var running = await _ollamaService.ListRunningModelsAsync();
            var models = await _ollamaService.ListModelsAsync();

            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.HealthStatus,
                Data = new Dictionary<string, object?>
                {
                    ["available"] = available,
                    ["runningModels"] = running.Select(m => new Dictionary<string, object?>
                    {
                        ["name"] = m.Name,
                        ["parameterSize"] = m.ParameterSize,
                        ["quantization"] = m.QuantizationLevel,
                        ["sizeMB"] = m.Size / (1024 * 1024),
                        ["vramMB"] = m.SizeVram / (1024 * 1024),
                        ["expiresAt"] = m.ExpiresAt.ToString("o")
                    }).ToList(),
                    ["installedModels"] = models.Select(m => new Dictionary<string, object?>
                    {
                        ["name"] = m.Name,
                        ["parameterSize"] = m.ParameterSize,
                        ["quantization"] = m.QuantizationLevel,
                        ["sizeMB"] = m.Size / (1024 * 1024)
                    }).ToList()
                }
            });
        }
        catch (Exception ex)
        {
            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.HealthStatus,
                Data = new Dictionary<string, object?>
                {
                    ["available"] = false,
                    ["error"] = ex.Message
                }
            });
        }
    }

    private async Task HandleModelInfoAsync()
    {
        if (_ollamaService is null) return;
        try
        {
            var info = await _ollamaService.ShowModelAsync();
            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.ModelInfoResponse,
                Data = info != null ? new Dictionary<string, object?>
                {
                    ["modelName"] = info.ModelName,
                    ["contextLength"] = info.ContextLength,
                    ["family"] = info.Family,
                    ["parameterSize"] = info.ParameterSize,
                    ["quantization"] = info.QuantizationLevel
                } : new Dictionary<string, object?> { ["error"] = "Model not found" }
            });
        }
        catch (Exception ex)
        {
            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.ModelInfoResponse,
                Data = new Dictionary<string, object?> { ["error"] = ex.Message }
            });
        }
    }

    private async Task HandleSettingsUpdateAsync(Dictionary<string, object?>? data)
    {
        if (data is null || _ollamaService is null) return;

        _ollamaService.UpdateOptions(opts =>
        {
            if (data.TryGetValue("model", out var model))
            {
                var m = model?.ToString();
                if (!string.IsNullOrEmpty(m)) opts.Model = m;
            }
            if (data.TryGetValue("temperature", out var temp) && temp is not null
                && double.TryParse(temp.ToString(), out var t))
                opts.Temperature = t;
            if (data.TryGetValue("ollamaUrl", out var url))
            {
                var u = url?.ToString();
                if (!string.IsNullOrEmpty(u)) opts.BaseUrl = u;
            }
            if (data.TryGetValue("cloudBaseUrl", out var cloudUrl))
            {
                var cu = cloudUrl?.ToString();
                if (!string.IsNullOrEmpty(cu)) opts.CloudBaseUrl = cu;
            }
            if (data.TryGetValue("cloudApiKey", out var cloudKey))
            {
                var ck = cloudKey?.ToString();
                if (!string.IsNullOrEmpty(ck)) opts.CloudApiKey = ck;
            }
            if (data.TryGetValue("cloudModel", out var cloudModel))
            {
                var cm = cloudModel?.ToString();
                if (!string.IsNullOrEmpty(cm)) opts.CloudModel = cm;
            }
            if (data.TryGetValue("think", out var think) && think is not null
                && bool.TryParse(think.ToString(), out var thinkVal))
                opts.Think = thinkVal;
            if (data.TryGetValue("logprobs", out var lp) && lp is not null
                && bool.TryParse(lp.ToString(), out var lpVal))
                opts.Logprobs = lpVal;
            if (data.TryGetValue("codeGenModel", out var cgm))
            {
                var cgmStr = cgm?.ToString();
                opts.CodeGenModel = string.IsNullOrWhiteSpace(cgmStr) ? null : cgmStr;
            }
        });

        _cloudRouter?.TryInitializeCodeGen();

        try
        {
            var opts = _ollamaService.GetCurrentOptions();
            var modelInfo = await _ollamaService.ShowModelAsync(opts.Model).ConfigureAwait(false);
            if (modelInfo is not null)
            {
                if (modelInfo.ContextLength > 0)
                {
                    var running = await _ollamaService.ListRunningModelsAsync().ConfigureAwait(false);
                    var isGpu = running.Any(m => m.SizeVram > m.Size / 2);
                    var effectiveCtx = isGpu
                        ? modelInfo.ContextLength
                        : Math.Min(modelInfo.ContextLength, 8192);
                    _contextOptimizer?.UpdateMaxTokens(effectiveCtx);
                    _ollamaService.UpdateOptions(o => o.NumCtx = effectiveCtx);
                }

                var usedCtx = opts.NumCtx ?? modelInfo.ContextLength;
                SendToUI(new BridgeMessage
                {
                    Type = BridgeMessageTypes.AssistantMessage,
                    Content = $"✅ Model **{opts.Model}** connected successfully " +
                              $"(ctx: {usedCtx}, {modelInfo.ParameterSize}, {modelInfo.QuantizationLevel})"
                });
            }
            else
            {
                SendToUI(new BridgeMessage
                {
                    Type = BridgeMessageTypes.Error,
                    Content = $"⚠️ Model \"{opts.Model}\" not found. Check if it is installed: ollama pull {opts.Model}"
                });
            }
        }
        catch (Exception ex)
        {
            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.Error,
                Content = $"⚠️ Cannot connect to Ollama at {_ollamaService.GetCurrentOptions().BaseUrl}: {ex.Message}"
            });
        }

        await HandleHealthCheckAsync().ConfigureAwait(false);
        await HandleRequestSettingsAsync().ConfigureAwait(false);
    }

    private void HandleAutomationModeChange(string modeName)
    {
        if (Enum.TryParse<AutomationMode>(modeName, ignoreCase: true, out var mode))
        {
            _contextCache.AutomationMode = mode;
            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.AutomationModeChanged,
                Content = mode.ToString()
            });
        }
    }

    private void HandleMemoryConsent(Dictionary<string, object?>? data)
    {
        if (data == null) return;
        if (data.TryGetValue("granted", out var grantedObj) && grantedObj is not null
            && bool.TryParse(grantedObj.ToString(), out var granted))
        {
            _contextCache.MemoryConsentGranted = granted;
            if (!granted)
                _memoryManager?.ClearLongTermMemory();
        }
    }

    private async Task HandleRequestSettingsAsync()
    {
        if (_ollamaService is null) return;
        try
        {
            var models = await _ollamaService.ListModelsAsync();
            var current = _ollamaService.GetCurrentOptions().Model;
            SendModelSync(current, models);
        }
        catch { }
    }

    private void HandleMemoryStatsRequest()
    {
        var stats = _memoryManager?.GetStats();
        SendToUI(new BridgeMessage
        {
            Type = BridgeMessageTypes.MemoryStats,
            Data = new Dictionary<string, object?>
            {
                ["shortTermEntries"] = stats?.ShortTermEntries ?? 0,
                ["longTermEntries"] = stats?.LongTermEntries ?? 0,
                ["consentGranted"] = stats?.ConsentGranted ?? false,
                ["oldestLongTermUtc"] = stats?.OldestLongTermUtc?.ToString("o"),
                ["nextExpiryUtc"] = stats?.NextExpiryUtc?.ToString("o")
            }
        });
    }

    /// <summary>
    /// Check if user has a dedicated CodeGen model. If not, suggest pulling one.
    /// Auto-detects existing coder models before suggesting a download.
    /// </summary>
    private async Task CheckCodeGenModelAsync()
    {
        if (_ollamaService is null) return;
        try
        {
            await Task.Delay(3000);

            var models = await _ollamaService.ListModelsAsync();
            var opts = _ollamaService.GetCurrentOptions();

            if (!string.IsNullOrEmpty(opts.CodeGenModel) &&
                models.Any(m => m.Name.Equals(opts.CodeGenModel, StringComparison.OrdinalIgnoreCase)))
            {
                _cloudRouter?.TryInitializeCodeGen();
                return;
            }

            var existingCoder = models.FirstOrDefault(m =>
                m.Name.Contains("coder", StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains("codellama", StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains("deepseek-coder", StringComparison.OrdinalIgnoreCase));

            if (existingCoder is not null)
            {
                _ollamaService.UpdateOptions(o => o.CodeGenModel = existingCoder.Name);
                _cloudRouter?.TryInitializeCodeGen();
                SendToUI(new BridgeMessage
                {
                    Type = BridgeMessageTypes.AssistantMessage,
                    Content = $"🔧 CodeGen model detected: **{existingCoder.Name}** — " +
                              "code generation will use this specialized model for better accuracy."
                });
                return;
            }

            var suggestOptions = RecommendedCodeGenModels.Select(r => new Dictionary<string, object?>
            {
                ["name"] = r.Name,
                ["description"] = r.Description,
                ["minVram"] = r.MinVram,
                ["installed"] = models.Any(m =>
                    m.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase))
            }).ToList();

            var suggested = RecommendedCodeGenModels[3];

            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.CodeGenModelSuggest,
                Content = $"Nâng cấp CodeGen? Model **{suggested.Name}** " +
                          "sẽ sinh code Revit API chính xác hơn nhiều so với model chat thường.",
                Data = new Dictionary<string, object?>
                {
                    ["modelName"] = suggested.Name,
                    ["description"] = suggested.Description,
                    ["minVram"] = suggested.MinVram,
                    ["options"] = suggestOptions
                }
            });
        }
        catch { }
    }

    private async Task HandleModelPullAsync(Dictionary<string, object?>? data)
    {
        if (data is null || _ollamaService is null) return;
        var modelName = data.TryGetValue("modelName", out var nameObj) ? nameObj?.ToString() : null;
        if (string.IsNullOrWhiteSpace(modelName)) return;

        var setAsCodeGen = data.TryGetValue("setAsCodeGen", out var flag)
            && bool.TryParse(flag?.ToString(), out var flagVal) && flagVal;

        _pullCts?.Cancel();
        _pullCts = new CancellationTokenSource();
        var ct = _pullCts.Token;

        try
        {
            await foreach (var progress in _ollamaService.PullModelAsync(modelName, ct))
            {
                SendToUI(new BridgeMessage
                {
                    Type = BridgeMessageTypes.ModelPullProgress,
                    Content = progress.Status,
                    Data = new Dictionary<string, object?>
                    {
                        ["modelName"] = modelName,
                        ["total"] = progress.Total,
                        ["completed"] = progress.Completed,
                        ["percent"] = Math.Round(progress.ProgressPercent, 1)
                    }
                });

                if (progress.IsComplete)
                {
                    if (setAsCodeGen)
                    {
                        _ollamaService.UpdateOptions(o => o.CodeGenModel = modelName);
                        _cloudRouter?.TryInitializeCodeGen();
                    }

                    SendToUI(new BridgeMessage
                    {
                        Type = BridgeMessageTypes.ModelPullComplete,
                        Content = $"Model **{modelName}** installed successfully!" +
                                  (setAsCodeGen ? " CodeGen will now use this model." : ""),
                        Data = new Dictionary<string, object?>
                        {
                            ["modelName"] = modelName,
                            ["setAsCodeGen"] = setAsCodeGen
                        }
                    });

                    var models = await _ollamaService.ListModelsAsync();
                    SendModelSync(_ollamaService.GetCurrentOptions().Model, models);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.ModelPullComplete,
                Content = $"Pull cancelled for {modelName}.",
                Data = new Dictionary<string, object?> { ["cancelled"] = true }
            });
        }
        catch (Exception ex)
        {
            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.Error,
                Content = $"Failed to pull {modelName}: {ex.Message}"
            });
        }
    }

    private void HandleCodeGenModelSet(Dictionary<string, object?>? data)
    {
        if (data is null || _ollamaService is null) return;
        var modelName = data.TryGetValue("modelName", out var nameObj) ? nameObj?.ToString() : null;
        if (string.IsNullOrWhiteSpace(modelName)) return;

        _ollamaService.UpdateOptions(o =>
            o.CodeGenModel = string.IsNullOrWhiteSpace(modelName) ? null : modelName);
        _cloudRouter?.TryInitializeCodeGen();

        SendToUI(new BridgeMessage
        {
            Type = BridgeMessageTypes.AssistantMessage,
            Content = string.IsNullOrWhiteSpace(modelName)
                ? "CodeGen model cleared — using main chat model for code generation."
                : $"🔧 CodeGen model set to **{modelName}**."
        });
    }

    private void SendToUI(BridgeMessage message)
    {
        var json = JsonSerializer.Serialize(message, _jsonOpts);
        _webView.Dispatcher.InvokeAsync(() =>
            _webView.CoreWebView2?.PostWebMessageAsString(json));
    }

    public void Dispose()
    {
        if (_webView.CoreWebView2 is not null)
            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;

        foreach (var sub in _hubSubscriptions)
        {
            try { sub.Dispose(); }
            catch { }
        }
        _hubSubscriptions.Clear();

        _chatSession?.StopSelfTraining();
        _ = SaveStateAsync();

        _pullCts?.Cancel();
        _pullCts?.Dispose();
        _vizManager?.Dispose();
        _selfTrainingScheduler?.Dispose();
        _agentLogger?.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task SaveStateAsync()
    {
        try
        {
            var timeout = Task.Delay(5000);

            Task saves;
            if (_persistenceManager != null)
            {
                saves = Task.WhenAll(
                    _chatSession?.PersistMemoryAsync() ?? Task.CompletedTask,
                    _persistenceManager.PersistAllAsync(),
                    _calcPreferenceStore?.SaveAsync() ?? Task.CompletedTask);
            }
            else
            {
                saves = Task.WhenAll(
                    _chatSession?.PersistMemoryAsync() ?? Task.CompletedTask,
                    _codeGenLibrary?.SaveAsync() ?? Task.CompletedTask,
                    _dynamicSkillRegistry?.SaveAsync() ?? Task.CompletedTask,
                    _patternLearning?.SaveAsync() ?? Task.CompletedTask,
                    _fewShotLearning?.SaveAsync() ?? Task.CompletedTask,
                    _dynamicGlossary?.SaveAsync() ?? Task.CompletedTask,
                    _calcPreferenceStore?.SaveAsync() ?? Task.CompletedTask);
            }

            await Task.WhenAny(saves, timeout);
        }
        catch { }
    }
}
