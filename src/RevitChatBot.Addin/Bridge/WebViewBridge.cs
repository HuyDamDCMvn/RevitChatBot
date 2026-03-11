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
    private SkillSuccessFeedback? _skillFeedback;
    private PromptCache? _promptCache;

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
        var skillRegistry = new SkillRegistry();
        var skillContext = new SkillContext
        {
            RevitDocument = _initData.Document,
            RevitApiInvoker = async (action) =>
                await _eventHandler.ExecuteAsync(doc => action(doc)),
            Extra = { ["contextCache"] = _contextCache }
        };
        var skillExecutor = new SkillExecutor(skillRegistry, skillContext);
        var contextManager = new ContextManager();
        contextManager.SetRevitDocument(_initData.Document);
        contextManager.SetContextCache(_contextCache);

        var addinDir = Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        _agentLogger = new AgentLogger(Path.Combine(addinDir, "logs"));

        RegisterMEPComponents(skillRegistry, contextManager);
        RegisterKnowledge(skillRegistry, contextManager);
        RegisterDynamicCode(skillRegistry);
        RegisterWebSkills(skillRegistry);
        RegisterVisionSkill(skillRegistry);
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
            promptCache: _promptCache,
            agentLogger: _agentLogger);

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
                _contextOptimizer.UpdateMaxTokens(info.ContextLength);
                _ollamaService.UpdateOptions(opts =>
                {
                    if (opts.NumCtx is null || opts.NumCtx < info.ContextLength)
                        opts.NumCtx = info.ContextLength;
                });
            }
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
            var memory = new MemoryManager(ollama, contextManager);
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
            var projectTitle = _initData.Document?.Title ?? "default";
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
            if (data.TryGetValue("cloudBaseUrl", out var cloudUrl) && cloudUrl is string cu)
                opts.CloudBaseUrl = cu;
            if (data.TryGetValue("cloudApiKey", out var cloudKey) && cloudKey is string ck)
                opts.CloudApiKey = ck;
            if (data.TryGetValue("cloudModel", out var cloudModel) && cloudModel is string cm)
                opts.CloudModel = cm;
            if (data.TryGetValue("think", out var think) && think is not null
                && bool.TryParse(think.ToString(), out var thinkVal))
                opts.Think = thinkVal;
            if (data.TryGetValue("logprobs", out var lp) && lp is not null
                && bool.TryParse(lp.ToString(), out var lpVal))
                opts.Logprobs = lpVal;
        });
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

        _agentLogger?.Dispose();
        if (_webView.CoreWebView2 is not null)
            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        GC.SuppressFinalize(this);
    }
}
