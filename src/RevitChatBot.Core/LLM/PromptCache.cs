using RevitChatBot.Core.CodeGen;
using RevitChatBot.Core.Learning;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// Pre-compiles and caches system prompt templates by intent.
/// Avoids rebuilding the full ~9000 token prompt on every call.
/// Only injects dynamic parts (context, history, few-shot examples).
/// Auto-invalidates when learning modules update (pattern_learned, glossary_updated).
/// </summary>
public class PromptCache
{
    private readonly Dictionary<string, string> _cachedPrompts = new();
    private string? _cheatSheet;
    private string? _errorFixes;
    private string? _codeExamples;
    private string? _fullStaticPrompt;
    private int _invalidationCount;

    /// <summary>
    /// Initialize cache with static content. Call once on startup.
    /// </summary>
    public void Initialize()
    {
        _cheatSheet = RevitApiCheatSheet.GetCheatSheet();
        _errorFixes = RevitApiCheatSheet.GetCommonErrorFixes();
        _codeExamples = CodeExamplesLibrary.GetExamples();
        _fullStaticPrompt = $"{_cheatSheet}\n\n{_errorFixes}\n\n{_codeExamples}";
    }

    /// <summary>
    /// Get the cached prompt for a given intent. Only includes relevant codegen content.
    /// </summary>
    public string GetBasePromptForIntent(string intent)
    {
        if (_cachedPrompts.TryGetValue(intent, out var cached))
            return cached;

        var result = intent switch
        {
            "check" or "analyze" or "query" or "explain" =>
                _cheatSheet ?? "",
            "create" or "calculate" or "modify" =>
                _fullStaticPrompt ?? "",
            _ => _fullStaticPrompt ?? ""
        };

        _cachedPrompts[intent] = result;
        return result;
    }

    /// <summary>
    /// Get full static prompt (for codegen-related queries).
    /// </summary>
    public string GetFullStaticPrompt() => _fullStaticPrompt ?? "";

    /// <summary>
    /// Invalidate cache (call if cheat sheet is updated).
    /// </summary>
    public void Invalidate()
    {
        _cachedPrompts.Clear();
        _fullStaticPrompt = null;
        Initialize();
    }

    public bool IsInitialized => _fullStaticPrompt != null;
    public int InvalidationCount => _invalidationCount;

    /// <summary>
    /// Subscribe to learning events that should trigger cache invalidation.
    /// Called once during initialization.
    /// </summary>
    public IDisposable? SubscribeToHub(LearningModuleHub hub)
    {
        return hub.Subscribe(
            [LearningEventTypes.PatternLearned, LearningEventTypes.GlossaryUpdated],
            _ =>
            {
                Interlocked.Increment(ref _invalidationCount);
                Invalidate();
            });
    }
}
