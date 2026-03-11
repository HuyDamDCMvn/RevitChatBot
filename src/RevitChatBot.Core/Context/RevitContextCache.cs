namespace RevitChatBot.Core.Context;

/// <summary>
/// In-memory cache of real-time Revit state, updated by event hooks
/// (SelectionChanged, ViewActivated, DocumentChanged).
/// Consumed by context providers for fast access without re-querying Revit API.
/// </summary>
public class RevitContextCache
{
    public ViewSnapshot? CurrentView { get; set; }
    public SelectionSnapshot? CurrentSelection { get; set; }
    public DocumentChangeDigest? LastDocumentChange { get; set; }

    public DateTime LastSelectionChangeUtc { get; set; }
    public DateTime LastViewChangeUtc { get; set; }
    public DateTime LastDocumentChangeUtc { get; set; }

    /// <summary>
    /// Automation mode per-session: controls whether LLM can call tools.
    /// </summary>
    public AutomationMode AutomationMode { get; set; } = AutomationMode.PlanAndApprove;

    /// <summary>
    /// Whether the user has consented to storing long-term memory (semantic facts).
    /// </summary>
    public bool MemoryConsentGranted { get; set; } = false;

    /// <summary>
    /// Extensible key-value store for module-specific context (e.g., visualization state).
    /// </summary>
    public Dictionary<string, object?> Extra { get; } = new();

    public event Action? OnContextChanged;

    public void NotifyChanged() => OnContextChanged?.Invoke();
}

public class ViewSnapshot
{
    public long ViewId { get; set; }
    public string ViewName { get; set; } = string.Empty;
    public string ViewType { get; set; } = string.Empty;
    public string? LevelName { get; set; }
    public int Scale { get; set; }
    public string? PhaseName { get; set; }
    public string? DisciplineName { get; set; }
}

public class SelectionSnapshot
{
    public List<long> ElementIds { get; set; } = [];
    public List<string> Categories { get; set; } = [];
    public int Count { get; set; }
}

public class DocumentChangeDigest
{
    public List<long> ModifiedIds { get; set; } = [];
    public List<long> AddedIds { get; set; } = [];
    public List<long> DeletedIds { get; set; } = [];
    public int TotalChanges => ModifiedIds.Count + AddedIds.Count + DeletedIds.Count;
}

public enum AutomationMode
{
    /// <summary>LLM only analyzes and suggests, no tool calls sent.</summary>
    SuggestOnly,

    /// <summary>LLM creates action plan, user reviews and approves before execution.</summary>
    PlanAndApprove,

    /// <summary>LLM can auto-execute, with confirmation for destructive actions only.</summary>
    AutoExecute
}
