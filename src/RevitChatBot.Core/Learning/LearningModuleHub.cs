namespace RevitChatBot.Core.Learning;

/// <summary>
/// Central event bus connecting all learning and codegen modules bidirectionally.
/// Each module publishes events here; subscribers react without direct coupling.
/// Replaces the previous pattern of each module needing to know every other module.
/// </summary>
public class LearningModuleHub
{
    private readonly Dictionary<string, object> _modules = new();
    private readonly List<(string[] EventTypes, Action<LearningEvent> Handler)> _subscriptions = [];
    private readonly object _lock = new();

    public event Action<LearningEvent>? OnEvent;

    public void Register(string moduleId, object module)
    {
        lock (_lock) { _modules[moduleId] = module; }
    }

    public T? Get<T>(string moduleId) where T : class
    {
        lock (_lock)
        {
            return _modules.GetValueOrDefault(moduleId) as T;
        }
    }

    public bool Has(string moduleId)
    {
        lock (_lock) { return _modules.ContainsKey(moduleId); }
    }

    /// <summary>
    /// Subscribe to specific event types. Handler is invoked for matching events only.
    /// Returns IDisposable that removes the subscription when disposed.
    /// </summary>
    public IDisposable Subscribe(string[] eventTypes, Action<LearningEvent> handler)
    {
        var entry = (eventTypes, handler);
        lock (_lock) { _subscriptions.Add(entry); }
        return new HubSubscription(this, entry);
    }

    /// <summary>
    /// Subscribe to ALL events.
    /// Returns IDisposable that removes the subscription when disposed.
    /// </summary>
    public IDisposable SubscribeAll(Action<LearningEvent> handler)
    {
        var entry = (Array.Empty<string>(), handler);
        lock (_lock) { _subscriptions.Add(entry); }
        return new HubSubscription(this, entry);
    }

    internal void Unsubscribe((string[] EventTypes, Action<LearningEvent> Handler) entry)
    {
        lock (_lock) { _subscriptions.Remove(entry); }
    }

    /// <summary>
    /// Publish an event to all matching subscribers. Non-blocking, exceptions are swallowed.
    /// </summary>
    public void Publish(LearningEvent evt)
    {
        OnEvent?.Invoke(evt);

        List<(string[] EventTypes, Action<LearningEvent> Handler)> snapshot;
        lock (_lock) { snapshot = [.. _subscriptions]; }

        foreach (var (eventTypes, handler) in snapshot)
        {
            if (eventTypes.Length == 0 || eventTypes.Contains(evt.EventType))
            {
                try { handler(evt); }
                catch { /* never crash the publisher */ }
            }
        }
    }

    /// <summary>
    /// Validate that all registered modules have their declared dependencies available.
    /// Returns list of missing connections for logging/debugging.
    /// </summary>
    public List<string> ValidateConnections()
    {
        var issues = new List<string>();
        lock (_lock)
        {
            foreach (var (id, module) in _modules)
            {
                if (module is ILearningModule lm)
                {
                    foreach (var dep in lm.DependsOn)
                    {
                        if (!_modules.ContainsKey(dep))
                            issues.Add($"Module '{id}' depends on '{dep}' which is not registered.");
                    }
                }
            }
        }
        return issues;
    }

    /// <summary>
    /// Get a dependency graph as text for debugging.
    /// </summary>
    public string GetDependencyGraph()
    {
        var lines = new List<string> { "=== Learning Module Dependency Graph ===" };
        lock (_lock)
        {
            foreach (var (id, module) in _modules)
            {
                if (module is ILearningModule lm)
                {
                    var deps = lm.DependsOn.Length > 0 ? string.Join(", ", lm.DependsOn) : "(none)";
                    var provides = lm.ProvidesTo.Length > 0 ? string.Join(", ", lm.ProvidesTo) : "(none)";
                    lines.Add($"  [{id}] depends on: {deps} | provides to: {provides}");
                }
                else
                {
                    lines.Add($"  [{id}] (no ILearningModule interface)");
                }
            }
        }
        return string.Join("\n", lines);
    }
}

/// <summary>
/// Event payload for the learning module hub.
/// </summary>
public record LearningEvent(
    string Source,
    string EventType,
    object? Data = null)
{
    public T? GetData<T>() where T : class => Data as T;
}

/// <summary>
/// Optional interface for modules that want to declare their dependencies
/// for validation and auto-wiring.
/// </summary>
public interface ILearningModule
{
    string ModuleId { get; }
    string[] DependsOn { get; }
    string[] ProvidesTo { get; }
}

/// <summary>
/// Well-known event type constants for the learning hub.
/// </summary>
public static class LearningEventTypes
{
    public const string CodeGenSuccess = "codegen_success";
    public const string CodeGenFailure = "codegen_failure";
    public const string PlanCompleted = "plan_completed";
    public const string SkillFailure = "skill_failure";
    public const string SkillRecovery = "skill_recovery";
    public const string SkillRegistered = "skill_registered";
    public const string KnowledgeIndexed = "knowledge_indexed";
    public const string ContextUsed = "context_used";
    public const string CompositeDiscovered = "composite_discovered";
    public const string GlossaryUpdated = "glossary_updated";
    public const string PatternLearned = "pattern_learned";
}

/// <summary>
/// Data payload for codegen events.
/// </summary>
public class CodeGenEventData
{
    public string Query { get; set; } = "";
    public string Code { get; set; } = "";
    public string? Description { get; set; }
    public string? Error { get; set; }
    public double ExecutionMs { get; set; }
    public string? Intent { get; set; }
    public string? Category { get; set; }
    public List<string> ApiPatternsUsed { get; set; } = [];
    public List<string> CategoriesQueried { get; set; } = [];
}

/// <summary>
/// Data payload for plan_completed events — enables cross-module learning from completed plans.
/// </summary>
public class PlanCompletedData
{
    public string Goal { get; set; } = "";
    public List<string> SkillsUsed { get; set; } = [];
    public string? Intent { get; set; }
    public string? Category { get; set; }
    public int StepCount { get; set; }
    public string? FinalAnswer { get; set; }
    public string? CodeGenCode { get; set; }
    public List<string>? KnowledgeSources { get; set; }
}

/// <summary>
/// Data payload for skill_recovery events — tracks successful fallback paths.
/// </summary>
public class SkillRecoveryData
{
    public string FailedSkill { get; set; } = "";
    public string RecoverySkill { get; set; } = "";
    public string FailureError { get; set; } = "";
}

public class SkillFailureData
{
    public string SkillName { get; set; } = "";
    public Dictionary<string, object?>? Arguments { get; set; }
    public string Error { get; set; } = "";
}

/// <summary>
/// Disposable subscription handle returned by LearningModuleHub.Subscribe.
/// </summary>
internal sealed class HubSubscription : IDisposable
{
    private LearningModuleHub? _hub;
    private readonly (string[] EventTypes, Action<LearningEvent> Handler) _entry;

    internal HubSubscription(
        LearningModuleHub hub,
        (string[] EventTypes, Action<LearningEvent> Handler) entry)
    {
        _hub = hub;
        _entry = entry;
    }

    public void Dispose()
    {
        _hub?.Unsubscribe(_entry);
        _hub = null;
    }
}
