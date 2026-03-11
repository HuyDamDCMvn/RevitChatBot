using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Calculation;

/// <summary>
/// Abstract base class for all Calculation Skills providing self-learning capabilities
/// isolated from other skill types. Uses SkillContext.Extra for storage — zero impact
/// on ISkill interface, SkillContext class, or non-Calculation skills.
///
/// Self-learning capabilities:
///   1. Parameter Memory — remembers user-preferred params per project
///   2. Delta Comparison — compares results with previous run
///   3. Follow-Up Suggestions — suggests next skills based on results
///   4. Smart Pagination — truncates large result sets with navigation hints
/// </summary>
public abstract class CalculationSkillBase : ISkill
{
    protected abstract string SkillName { get; }

    public abstract Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);

    #region Capability 1: Parameter Memory

    /// <summary>
    /// Resolve a parameter value with priority: explicit param → saved preference → fallback.
    /// When user provides a value explicitly, it is saved as the new preference.
    /// </summary>
    protected double GetParamDouble(
        Dictionary<string, object?> parameters,
        SkillContext context,
        string paramName,
        double fallback)
    {
        if (parameters.TryGetValue(paramName, out var val) && val is not null)
        {
            var parsed = ParseDouble(val, fallback);
            GetStore(context)?.SavePreference(SkillName, paramName, parsed);
            return parsed;
        }

        var saved = GetStore(context)?.GetPreference(SkillName, paramName);
        if (saved is not null && double.TryParse(saved.ToString(), out var savedVal))
            return savedVal;

        return fallback;
    }

    /// <summary>
    /// Resolve a string parameter with priority: explicit → saved preference → fallback.
    /// </summary>
    protected string GetParamString(
        Dictionary<string, object?> parameters,
        SkillContext context,
        string paramName,
        string fallback)
    {
        if (parameters.TryGetValue(paramName, out var val) && val is not null)
        {
            var str = val.ToString() ?? fallback;
            GetStore(context)?.SavePreference(SkillName, paramName, str);
            return str;
        }

        var saved = GetStore(context)?.GetPreference(SkillName, paramName);
        if (saved is not null)
            return saved.ToString() ?? fallback;

        return fallback;
    }

    #endregion

    #region Capability 2: Delta Comparison

    /// <summary>
    /// Compare current result summary with the previous run's summary.
    /// Returns null on first run (no previous data).
    /// </summary>
    protected CalcDeltaReport? ComputeDelta(SkillContext context, CalcResultSummary current)
    {
        var store = GetStore(context);
        var previous = store?.GetPreviousResult(SkillName);
        if (previous is null) return null;

        var delta = new CalcDeltaReport
        {
            PreviousRunUtc = previous.Timestamp,
            TotalItemsBefore = previous.TotalItems,
            TotalItemsAfter = current.TotalItems,
            IssueCountBefore = previous.IssueCount,
            IssueCountAfter = current.IssueCount,
            IssueCountDelta = current.IssueCount - previous.IssueCount,
        };
        delta.Summary = BuildDeltaSummary(delta);
        return delta;
    }

    /// <summary>
    /// Save current result summary for future delta comparisons.
    /// </summary>
    protected void SaveResultForDelta(SkillContext context, CalcResultSummary current)
    {
        GetStore(context)?.SaveResult(SkillName, current);
    }

    private static string BuildDeltaSummary(CalcDeltaReport delta)
    {
        if (delta.IssueCountDelta == 0)
            return $"No change since last run ({delta.PreviousRunUtc:g}): {delta.IssueCountAfter} issues.";

        var direction = delta.IssueCountDelta > 0 ? "increased" : "decreased";
        var pct = delta.IssueCountBefore > 0
            ? Math.Abs(delta.IssueCountDelta) * 100.0 / delta.IssueCountBefore
            : 100.0;

        return $"Issues {direction} from {delta.IssueCountBefore} to {delta.IssueCountAfter} " +
               $"({(delta.IssueCountDelta > 0 ? "+" : "")}{delta.IssueCountDelta}, " +
               $"{pct:F0}% change since {delta.PreviousRunUtc:g}).";
    }

    #endregion

    #region Capability 3: Follow-Up Suggestions

    /// <summary>
    /// Append follow-up skill suggestions to the result message.
    /// The LLM reads these and can proactively suggest next steps to the user.
    /// </summary>
    protected static string AppendFollowUps(string message, List<FollowUpSuggestion> suggestions)
    {
        if (suggestions.Count == 0) return message;

        var block = "\n\n[Suggested next steps]\n" +
            string.Join("\n", suggestions.Select(s =>
            {
                var paramHint = s.PrefilledParams.Count > 0
                    ? " with " + string.Join(", ", s.PrefilledParams.Select(kv => $"{kv.Key}=\"{kv.Value}\""))
                    : "";
                return $"  - {s.Reason}: use '{s.SkillName}'{paramHint}";
            }));

        return message + block;
    }

    #endregion

    #region Capability 4: Smart Pagination

    /// <summary>
    /// Return a paginated result with a hint about hidden items.
    /// </summary>
    protected static SkillResult OkPaginated(
        string message, object fullData,
        int totalItems, int shownItems, string itemLabel = "items")
    {
        if (totalItems > shownItems)
            message += $"\n(Showing top {shownItems} of {totalItems} {itemLabel}. " +
                       "Ask for more details or filter by level/system to narrow down.)";

        return SkillResult.Ok(message, fullData);
    }

    #endregion

    #region Internal Helpers

    private static CalculationPreferenceStore? GetStore(SkillContext context)
    {
        return context.Extra.GetValueOrDefault("calc_preference_store")
            as CalculationPreferenceStore;
    }

    protected static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is long l) return l;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }

    protected static int ParseInt(object? value, int fallback)
    {
        if (value is int i) return i;
        if (value is long l) return (int)l;
        if (value is double d) return (int)d;
        if (value is string s && int.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }

    #endregion
}

#region Data Models

public class CalcResultSummary
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int TotalItems { get; set; }
    public int IssueCount { get; set; }
    public Dictionary<string, double> KeyMetrics { get; set; } = new();
}

public class CalcDeltaReport
{
    public DateTime PreviousRunUtc { get; set; }
    public int TotalItemsBefore { get; set; }
    public int TotalItemsAfter { get; set; }
    public int IssueCountBefore { get; set; }
    public int IssueCountAfter { get; set; }
    public int IssueCountDelta { get; set; }
    public string Summary { get; set; } = "";
}

public class FollowUpSuggestion
{
    public string SkillName { get; set; } = "";
    public string Reason { get; set; } = "";
    public Dictionary<string, object?> PrefilledParams { get; set; } = new();
}

#endregion
