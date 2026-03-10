using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Autodesk.Revit.UI;
using RevitChatBot.Addin.Handlers;
using RevitChatBot.Core.Agent;
using RevitChatBot.Core.CodeGen;
using RevitChatBot.Core.Context;
using RevitChatBot.Core.LLM;
using RevitChatBot.Core.Memory;
using RevitChatBot.Core.Models;
using RevitChatBot.Core.Skills;
using RevitChatBot.Knowledge.Embeddings;
using RevitChatBot.Knowledge.Search;
using RevitChatBot.Knowledge.VectorStore;

namespace RevitChatBot.Addin.Bridge;

public class WebViewBridge : IDisposable
{
    private readonly WebView2 _webView;
    private readonly UIApplication _uiApp;
    private ChatSessionV2? _chatSession;
    private OllamaService? _ollamaService;
    private RevitEventHandler? _eventHandler;
    private KnowledgeManager? _knowledgeManager;
    private KnowledgeContextProvider? _knowledgeContextProvider;
    private MemoryManager? _memoryManager;
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
    private SkillSuccessFeedback? _skillFeedback;
    private PromptCache? _promptCache;

    public WebViewBridge(WebView2 webView, UIApplication uiApp)
    {
        _webView = webView;
        _uiApp = uiApp;
    }

    public void Initialize()
    {
        _eventHandler = new RevitEventHandler();
        _eventHandler.Register(_uiApp);

        _ollamaService = new OllamaService();
        var skillRegistry = new SkillRegistry();
        var skillContext = new SkillContext
        {
            RevitDocument = _uiApp.ActiveUIDocument?.Document,
            RevitApiInvoker = async (action) =>
                await _eventHandler.ExecuteAsync(doc => action(doc))
        };
        var skillExecutor = new SkillExecutor(skillRegistry, skillContext);
        var contextManager = new ContextManager();
        contextManager.SetRevitDocument(_uiApp.ActiveUIDocument?.Document);

        RegisterMEPComponents(skillRegistry, contextManager);
        RegisterKnowledge(skillRegistry, contextManager);
        RegisterDynamicCode(skillRegistry);
        InitializeQueryUnderstanding(skillRegistry);
        InitializeLLMIntelligence();

        _memoryManager = InitializeMemory(_ollamaService, contextManager);

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
            promptCache: _promptCache);

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

        _chatSession.OnConfirmationRequired += async description =>
        {
            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.ConfirmationRequired,
                Content = description
            });
            return true;
        };

        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _ = InitializeKnowledgeAsync();
        _ = InitializeMemoryAsync();
        _ = InitializeCodeGenModulesAsync();
        _ = InitializeLLMModulesAsync();
    }

    private static void RegisterMEPComponents(
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
            contextManager.Register(new MEP.Context.SelectedElementsProvider());
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

            _codeExecutor = new DynamicCodeExecutor(compiler);

            var addinDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            var codegenDir = Path.Combine(addinDir, "codegen");
            Directory.CreateDirectory(codegenDir);

            _codeGenLibrary = new CodeGenLibrary(Path.Combine(codegenDir, "codegen_library.json"));
            _dynamicSkillRegistry = new DynamicSkillRegistry(
                Path.Combine(codegenDir, "dynamic_skills.json"), _codeExecutor, registry);
            _patternLearning = new CodePatternLearning(Path.Combine(codegenDir, "patterns.json"));

            var skill = new DynamicCodeSkill(_codeExecutor, _codeGenLibrary, _dynamicSkillRegistry, _patternLearning);
            registry.Register(skill);
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
        }
        catch { }
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
            var memoryDir = Path.Combine(addinDir, "memory");
            var memory = new MemoryManager(memoryDir, ollama);

            contextManager.Register(new MemoryContextProvider(memory));

            _skillFeedback = new SkillSuccessFeedback(memory.Analytics);

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
            var projectTitle = _uiApp.ActiveUIDocument?.Document?.Title ?? "default";
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
            await Task.WhenAll(tasks);
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
            var message = JsonSerializer.Deserialize<BridgeMessage>(json, _jsonOpts);
            if (message is null) return;

            switch (message.Type)
            {
                case BridgeMessageTypes.UserMessage:
                    await HandleUserMessage(message.Content ?? "");
                    break;

                case BridgeMessageTypes.SettingsUpdate:
                    HandleSettingsUpdate(message.Data);
                    break;

                case "partial_input":
                    HandlePartialInput(message.Content ?? "");
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

        try
        {
            _knowledgeContextProvider?.SetQuery(content);

            var response = await _chatSession.SendMessageAsync(content);

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

            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.AssistantMessage,
                Content = response
            });
        }
        catch (Exception ex)
        {
            SendToUI(new BridgeMessage
            {
                Type = BridgeMessageTypes.Error,
                Content = $"Error: {ex.Message}"
            });
        }
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

    private void HandleSettingsUpdate(Dictionary<string, object?>? data)
    {
        if (data is null || _ollamaService is null) return;

        _ollamaService.UpdateOptions(opts =>
        {
            if (data.TryGetValue("model", out var model) && model is string m)
                opts.Model = m;
            if (data.TryGetValue("temperature", out var temp) && temp is not null
                && double.TryParse(temp.ToString(), out var t))
                opts.Temperature = t;
            if (data.TryGetValue("ollamaUrl", out var url) && url is string u)
                opts.BaseUrl = u;
        });
    }

    private void SendToUI(BridgeMessage message)
    {
        var json = JsonSerializer.Serialize(message, _jsonOpts);
        _webView.Dispatcher.InvokeAsync(() =>
            _webView.CoreWebView2?.PostWebMessageAsJson(json));
    }

    public void Dispose()
    {
        try
        {
            _chatSession?.PersistMemoryAsync().GetAwaiter().GetResult();
            _codeGenLibrary?.SaveAsync().GetAwaiter().GetResult();
            _dynamicSkillRegistry?.SaveAsync().GetAwaiter().GetResult();
            _patternLearning?.SaveAsync().GetAwaiter().GetResult();
            _fewShotLearning?.SaveAsync().GetAwaiter().GetResult();
            _dynamicGlossary?.SaveAsync().GetAwaiter().GetResult();
        }
        catch { }

        _eventHandler?.Unregister();
        if (_webView.CoreWebView2 is not null)
            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        GC.SuppressFinalize(this);
    }
}
