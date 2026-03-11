using System.Text.Json;

namespace RevitChatBot.Core.Learning;

/// <summary>
/// Learns from skill execution failures to build a recovery knowledge base.
/// While SelfEvaluator evaluates completed plans, this module focuses on
/// individual skill failures within a plan to learn:
///
///   1. COMMON FAILURE MODES: "execute_revit_code fails 40% with NullReferenceException"
///      → Agent should add null checks to generated code
///
///   2. RECOVERY PATHS: "When query_elements fails, execute_revit_code succeeds 80%"
///      → Agent learns fallback strategies
///
///   3. PARAMETER SENSITIVITY: "check_clearance fails when clearance_mm < 50"
///      → Agent learns parameter bounds
///
///   4. PRECONDITION DISCOVERY: "query_duct_sizing fails without active view"
///      → Agent learns implicit preconditions
///
///   5. RETRY STRATEGIES: "Same skill with adjusted parameters succeeds 60%"
///      → Agent learns when to retry vs fallback
///
/// This feeds into CrossSkillCorrelator (fallback paths) and ProactiveSuggestionEngine.
/// </summary>
public class FailureRecoveryLearner
{
    private readonly string _filePath;
    private FailureKnowledgeBase _kb = new();

    public FailureRecoveryLearner(string dataDir)
    {
        _filePath = Path.Combine(dataDir, "failure_recovery.json");
    }

    /// <summary>
    /// Record a skill failure with context.
    /// </summary>
    public void RecordFailure(
        string skillName,
        Dictionary<string, object?>? parameters,
        string errorMessage,
        string? errorType = null)
    {
        if (!_kb.FailureModes.TryGetValue(skillName, out var modes))
        {
            modes = [];
            _kb.FailureModes[skillName] = modes;
        }

        var normalizedError = NormalizeErrorMessage(errorMessage);
        var existing = modes.FirstOrDefault(m => m.NormalizedError == normalizedError);
        if (existing is not null)
        {
            existing.OccurrenceCount++;
            existing.LastSeen = DateTime.UtcNow;
        }
        else
        {
            modes.Add(new FailureMode
            {
                NormalizedError = normalizedError,
                ErrorType = errorType ?? ExtractErrorType(errorMessage),
                ExampleParameters = parameters?.ToDictionary(
                    kv => kv.Key, kv => kv.Value?.ToString() ?? ""),
                OccurrenceCount = 1,
                FirstSeen = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Record a successful recovery: skill A failed, skill B succeeded.
    /// </summary>
    public void RecordRecovery(
        string failedSkill, string recoverySkill,
        string failureError, bool recoverySucceeded)
    {
        var key = $"{failedSkill}→{recoverySkill}";
        if (!_kb.RecoveryPaths.TryGetValue(key, out var path))
        {
            path = new RecoveryPath
            {
                FailedSkill = failedSkill,
                RecoverySkill = recoverySkill,
                FailurePattern = NormalizeErrorMessage(failureError)
            };
            _kb.RecoveryPaths[key] = path;
        }

        path.TotalAttempts++;
        if (recoverySucceeded) path.SuccessCount++;
    }

    /// <summary>
    /// Record a successful retry with adjusted parameters.
    /// </summary>
    public void RecordRetrySuccess(
        string skillName, string originalError,
        Dictionary<string, object?>? adjustedParameters)
    {
        var key = $"{skillName}:{NormalizeErrorMessage(originalError)}";
        if (!_kb.RetryStrategies.TryGetValue(key, out var strategy))
        {
            strategy = new RetryStrategy
            {
                SkillName = skillName,
                FailurePattern = NormalizeErrorMessage(originalError)
            };
            _kb.RetryStrategies[key] = strategy;
        }

        strategy.SuccessCount++;
        if (adjustedParameters is not null)
        {
            var paramHint = string.Join(", ", adjustedParameters
                .Where(kv => kv.Value is not null)
                .Select(kv => $"{kv.Key}={kv.Value}"));
            if (!string.IsNullOrWhiteSpace(paramHint))
                strategy.ParameterAdjustmentHints.Add(paramHint);
        }
    }

    /// <summary>
    /// Get recovery advice for a failed skill.
    /// Returns null if no recovery knowledge is available.
    /// </summary>
    public string? GetRecoveryAdvice(string failedSkill, string errorMessage)
    {
        var normalizedError = NormalizeErrorMessage(errorMessage);
        var advice = new List<string>();

        // Find known failure modes
        if (_kb.FailureModes.TryGetValue(failedSkill, out var modes))
        {
            var matchingMode = modes
                .OrderByDescending(m => m.OccurrenceCount)
                .FirstOrDefault(m => normalizedError.Contains(m.NormalizedError) ||
                                     m.NormalizedError.Contains(normalizedError));

            if (matchingMode is not null && matchingMode.OccurrenceCount >= 2)
                advice.Add($"Known issue: '{failedSkill}' frequently fails with " +
                    $"'{matchingMode.ErrorType}' ({matchingMode.OccurrenceCount}x).");
        }

        // Find recovery paths
        var recoveryPaths = _kb.RecoveryPaths.Values
            .Where(p => p.FailedSkill == failedSkill && p.SuccessRate > 0.5)
            .OrderByDescending(p => p.SuccessRate)
            .Take(2);

        foreach (var path in recoveryPaths)
            advice.Add($"Try '{path.RecoverySkill}' instead " +
                $"(recovery success rate: {path.SuccessRate:P0}).");

        // Find retry strategies
        var retries = _kb.RetryStrategies.Values
            .Where(s => s.SkillName == failedSkill && s.SuccessCount >= 2)
            .Take(1);

        foreach (var strategy in retries)
        {
            var hint = strategy.ParameterAdjustmentHints.LastOrDefault();
            if (hint is not null)
                advice.Add($"Retry '{failedSkill}' with adjusted parameters: {hint}.");
        }

        return advice.Count > 0 ? string.Join(" ", advice) : null;
    }

    /// <summary>
    /// Build a prompt section with failure recovery knowledge.
    /// </summary>
    public string BuildRecoveryContext(int maxEntries = 5)
    {
        var lines = new List<string>();

        var topFailures = _kb.FailureModes
            .SelectMany(kv => kv.Value.Select(m => (Skill: kv.Key, Mode: m)))
            .Where(x => x.Mode.OccurrenceCount >= 3)
            .OrderByDescending(x => x.Mode.OccurrenceCount)
            .Take(maxEntries);

        foreach (var (skill, mode) in topFailures)
            lines.Add($"  • '{skill}' → {mode.ErrorType}: " +
                $"occurred {mode.OccurrenceCount}x");

        var topRecoveries = _kb.RecoveryPaths.Values
            .Where(p => p.TotalAttempts >= 2 && p.SuccessRate > 0.5)
            .OrderByDescending(p => p.SuccessRate)
            .Take(maxEntries);

        foreach (var path in topRecoveries)
            lines.Add($"  • When '{path.FailedSkill}' fails, use '{path.RecoverySkill}' " +
                $"({path.SuccessRate:P0} success)");

        if (lines.Count == 0) return "";
        return "--- FAILURE RECOVERY (learned) ---\n" + string.Join("\n", lines);
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(_kb, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch { /* non-critical */ }
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, ct);
                _kb = JsonSerializer.Deserialize<FailureKnowledgeBase>(json, JsonOpts) ?? new();
            }
        }
        catch { _kb = new(); }
    }

    private static string NormalizeErrorMessage(string error)
    {
        if (error.Length > 100) error = error[..100];
        return error.ToLowerInvariant().Trim();
    }

    private static string ExtractErrorType(string error)
    {
        if (error.Contains("NullReference")) return "NullReferenceException";
        if (error.Contains("InvalidOperation")) return "InvalidOperationException";
        if (error.Contains("timeout", StringComparison.OrdinalIgnoreCase)) return "Timeout";
        if (error.Contains("not found", StringComparison.OrdinalIgnoreCase)) return "NotFound";
        if (error.Contains("permission", StringComparison.OrdinalIgnoreCase)) return "Permission";
        if (error.Contains("compilation", StringComparison.OrdinalIgnoreCase)) return "CompilationError";
        return "Unknown";
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public class FailureKnowledgeBase
{
    public Dictionary<string, List<FailureMode>> FailureModes { get; set; } = new();
    public Dictionary<string, RecoveryPath> RecoveryPaths { get; set; } = new();
    public Dictionary<string, RetryStrategy> RetryStrategies { get; set; } = new();
}

public class FailureMode
{
    public string NormalizedError { get; set; } = "";
    public string ErrorType { get; set; } = "";
    public Dictionary<string, string>? ExampleParameters { get; set; }
    public int OccurrenceCount { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
}

public class RecoveryPath
{
    public string FailedSkill { get; set; } = "";
    public string RecoverySkill { get; set; } = "";
    public string FailurePattern { get; set; } = "";
    public int TotalAttempts { get; set; }
    public int SuccessCount { get; set; }
    public double SuccessRate => TotalAttempts > 0 ? (double)SuccessCount / TotalAttempts : 0;
}

public class RetryStrategy
{
    public string SkillName { get; set; } = "";
    public string FailurePattern { get; set; } = "";
    public int SuccessCount { get; set; }
    public List<string> ParameterAdjustmentHints { get; set; } = [];
}
