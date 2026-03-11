namespace RevitChatBot.Core.Agent;

/// <summary>
/// Tracks Revit model warnings before/after tool execution.
/// Detects if an action introduced new warnings or resolved existing ones.
/// </summary>
public class WarningsDeltaTracker
{
    private int _warningsBefore;
    private List<string> _warningDetailsBefore = [];

    /// <summary>
    /// Capture warning count before tool execution via RevitApiInvoker.
    /// </summary>
    public void CaptureBeforeState(int warningCount, List<string>? warningDetails = null)
    {
        _warningsBefore = warningCount;
        _warningDetailsBefore = warningDetails ?? [];
    }

    /// <summary>
    /// Compare with after-state and return delta info.
    /// </summary>
    public WarningsDelta ComputeDelta(int warningsAfter, List<string>? warningDetailsAfter = null)
    {
        var detailsAfter = warningDetailsAfter ?? [];
        var newWarnings = detailsAfter.Except(_warningDetailsBefore).ToList();
        var resolvedWarnings = _warningDetailsBefore.Except(detailsAfter).ToList();

        return new WarningsDelta
        {
            WarningsBefore = _warningsBefore,
            WarningsAfter = warningsAfter,
            Delta = warningsAfter - _warningsBefore,
            NewWarnings = newWarnings,
            ResolvedWarnings = resolvedWarnings,
            IsRegression = warningsAfter > _warningsBefore
        };
    }
}

public class WarningsDelta
{
    public int WarningsBefore { get; set; }
    public int WarningsAfter { get; set; }
    public int Delta { get; set; }
    public List<string> NewWarnings { get; set; } = [];
    public List<string> ResolvedWarnings { get; set; } = [];
    public bool IsRegression { get; set; }

    public string ToSummary()
    {
        if (Delta == 0) return "No change in model warnings.";
        if (Delta > 0) return $"⚠ {Delta} new warning(s) introduced. {string.Join("; ", NewWarnings.Take(3))}";
        return $"✓ {-Delta} warning(s) resolved. {string.Join("; ", ResolvedWarnings.Take(3))}";
    }
}
