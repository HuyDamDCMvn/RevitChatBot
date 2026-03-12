using System.Text.Json;
using RevitChatBot.Core.Learning;

namespace RevitChatBot.Core.CodeGen;

/// <summary>
/// Module 3: Learns from codegen history to improve future code generation.
/// Tracks error patterns, successful API usage, and auto-generates
/// additional error fixes and usage hints for the LLM.
/// Bidirectional: subscribes to codegen events, publishes learned error fixes.
/// </summary>
public class CodePatternLearning
{
    private readonly string _filePath;
    private readonly LearningModuleHub? _hub;
    private CodePatternData _data = new();
    private bool _loaded;

    public CodePatternLearning(string filePath, LearningModuleHub? hub = null)
    {
        _filePath = filePath;
        _hub = hub;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_loaded) return;
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, ct);
                _data = JsonSerializer.Deserialize<CodePatternData>(json, JsonOpts) ?? new();
            }
        }
        catch { _data = new(); }
        _loaded = true;
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_data, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch { /* non-critical */ }
    }

    /// <summary>
    /// Record a successful codegen execution.
    /// Extracts API patterns used (categories queried, methods called, etc).
    /// </summary>
    public void RecordSuccess(string code, string description, double executionMs)
    {
        _data.TotalSuccesses++;
        _data.TotalExecutionMs += executionMs;

        var apis = ExtractApiPatterns(code);
        foreach (var api in apis)
        {
            _data.ApiUsageCount[api] = _data.ApiUsageCount.GetValueOrDefault(api) + 1;
        }

        var cats = ExtractCategories(code);
        foreach (var cat in cats)
        {
            _data.CategoryQueryCount[cat] = _data.CategoryQueryCount.GetValueOrDefault(cat) + 1;
        }
    }

    /// <summary>
    /// Record a failed codegen attempt. Extracts error pattern for auto-fix suggestions.
    /// </summary>
    public void RecordFailure(string code, string error)
    {
        _data.TotalFailures++;

        var pattern = ExtractErrorPattern(error);
        if (string.IsNullOrEmpty(pattern)) return;

        if (!_data.ErrorPatterns.TryGetValue(pattern, out var ep))
        {
            ep = new ErrorPatternEntry { Pattern = pattern };
            _data.ErrorPatterns[pattern] = ep;
        }
        ep.Count++;
        ep.LastSeen = DateTime.UtcNow;
        ep.LastError = error.Length > 200 ? error[..200] : error;

        if (_data.ErrorPatterns.Count > 100)
        {
            var toRemove = _data.ErrorPatterns
                .Where(kv => kv.Value.Count == 1 && kv.Value.LastSeen < DateTime.UtcNow.AddDays(-30))
                .Select(kv => kv.Key).Take(20).ToList();
            foreach (var key in toRemove)
                _data.ErrorPatterns.Remove(key);
        }
    }

    /// <summary>
    /// Record a user-provided fix that resolved a codegen error.
    /// This creates a learned error-fix pair.
    /// </summary>
    public void RecordFix(string errorPattern, string fix)
    {
        if (!_data.ErrorPatterns.TryGetValue(errorPattern, out var ep))
        {
            ep = new ErrorPatternEntry { Pattern = errorPattern };
            _data.ErrorPatterns[errorPattern] = ep;
        }
        ep.LearnedFix = fix;
        ep.FixConfirmed = true;

        _hub?.Publish(new LearningEvent("PatternLearning",
            LearningEventTypes.CodeGenSuccess,
            new CodeGenEventData
            {
                Query = $"[fix_learned] {errorPattern}",
                Code = fix,
                Description = $"Learned fix for pattern: {errorPattern}"
            }));
    }

    /// <summary>
    /// Get auto-generated error fixes based on learned patterns.
    /// Injected into the LLM context alongside the static CommonErrorFixes.
    /// </summary>
    public string GetLearnedErrorFixes()
    {
        var fixedPatterns = _data.ErrorPatterns.Values
            .Where(ep => ep.FixConfirmed && !string.IsNullOrEmpty(ep.LearnedFix))
            .OrderByDescending(ep => ep.Count)
            .Take(20);

        if (!fixedPatterns.Any()) return "";

        var lines = new List<string> { "=== LEARNED ERROR FIXES (from project history) ===" };
        foreach (var ep in fixedPatterns)
        {
            lines.Add($"\nERROR PATTERN: \"{ep.Pattern}\"");
            lines.Add($"FIX: {ep.LearnedFix}");
            lines.Add($"(seen {ep.Count}x)");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Get top error patterns that don't have a fix yet (frequent failures).
    /// Useful for the LLM to proactively avoid these patterns.
    /// </summary>
    public string GetFrequentErrorWarnings()
    {
        var unfixed = _data.ErrorPatterns.Values
            .Where(ep => !ep.FixConfirmed && ep.Count >= 2)
            .OrderByDescending(ep => ep.Count)
            .Take(10);

        if (!unfixed.Any()) return "";

        var lines = new List<string> { "=== KNOWN ERROR-PRONE PATTERNS (avoid these) ===" };
        foreach (var ep in unfixed)
        {
            lines.Add($"  AVOID: \"{ep.Pattern}\" (failed {ep.Count}x) — {ep.LastError}");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Get the most commonly used API patterns (helps LLM prioritize).
    /// </summary>
    public string GetApiUsageSummary()
    {
        if (_data.ApiUsageCount.Count == 0) return "";

        var top = _data.ApiUsageCount
            .OrderByDescending(kv => kv.Value)
            .Take(15);

        var lines = new List<string> { "[codegen_api_usage]" };
        foreach (var (api, count) in top)
            lines.Add($"  {api}: {count}x");

        double successRate = _data.TotalSuccesses + _data.TotalFailures > 0
            ? (double)_data.TotalSuccesses / (_data.TotalSuccesses + _data.TotalFailures) * 100 : 0;
        lines.Add($"  Success rate: {Math.Round(successRate, 1)}% ({_data.TotalSuccesses}/{_data.TotalSuccesses + _data.TotalFailures})");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Get confirmed error→fix pairs for programmatic use (e.g., CodeAutoFixer integration).
    /// </summary>
    public Dictionary<string, string> GetConfirmedFixes()
    {
        return _data.ErrorPatterns.Values
            .Where(ep => ep.FixConfirmed && !string.IsNullOrEmpty(ep.LearnedFix))
            .ToDictionary(ep => ep.Pattern, ep => ep.LearnedFix!);
    }

    /// <summary>
    /// Combined output for LLM context injection.
    /// </summary>
    public string GetFullContext()
    {
        var parts = new List<string>();

        var fixes = GetLearnedErrorFixes();
        if (!string.IsNullOrEmpty(fixes)) parts.Add(fixes);

        var warnings = GetFrequentErrorWarnings();
        if (!string.IsNullOrEmpty(warnings)) parts.Add(warnings);

        var usage = GetApiUsageSummary();
        if (!string.IsNullOrEmpty(usage)) parts.Add(usage);

        return string.Join("\n\n", parts);
    }

    private static string ExtractErrorPattern(string error)
    {
        if (error.Contains("CS0246") || error.Contains("could not be found"))
            return "missing_namespace_or_type";
        if (error.Contains("CS0103") || error.Contains("does not exist"))
            return "undefined_name";
        if (error.Contains("CS0029") || error.Contains("Cannot implicitly convert"))
            return "type_conversion";
        if (error.Contains("CS1061") || error.Contains("does not contain a definition"))
            return "missing_member";
        if (error.Contains("NullReferenceException"))
            return "null_reference";
        if (error.Contains("InvalidOperationException"))
            return "invalid_operation";
        if (error.Contains("Transaction") || error.Contains("not permitted"))
            return "missing_transaction";
        if (error.Contains("obsolete") || error.Contains("deprecated"))
            return "deprecated_api";
        if (error.Contains("Security validation"))
            return "security_block";
        if (error.Contains("Sequence contains no"))
            return "empty_collection";

        var firstLine = error.Split('\n')[0];
        if (firstLine.Length > 80) firstLine = firstLine[..80];
        return firstLine.Trim();
    }

    private static List<string> ExtractApiPatterns(string code)
    {
        var patterns = new List<string>();

        if (code.Contains("FilteredElementCollector")) patterns.Add("FilteredElementCollector");
        if (code.Contains("Transaction")) patterns.Add("Transaction");
        if (code.Contains("ConnectorManager")) patterns.Add("ConnectorManager");
        if (code.Contains("ReferenceIntersector")) patterns.Add("ReferenceIntersector");
        if (code.Contains("BoundingBox")) patterns.Add("BoundingBoxCheck");
        if (code.Contains("InsulationLiningBase")) patterns.Add("InsulationCheck");
        if (code.Contains("BreakCurve")) patterns.Add("BreakCurve");
        if (code.Contains("IsPointInRoom") || code.Contains("IsPointInSpace")) patterns.Add("SpatialQuery");
        if (code.Contains("NewElbowFitting") || code.Contains("NewTeeFitting")) patterns.Add("FittingCreation");
        if (code.Contains("Pipe.Create") || code.Contains("Duct.Create")) patterns.Add("ElementCreation");
        if (code.Contains("OverrideGraphicSettings")) patterns.Add("ViewOverride");
        if (code.Contains("GetWarnings")) patterns.Add("ModelAudit");
        if (code.Contains("UnitUtils.Convert")) patterns.Add("UnitConversion");
        if (code.Contains("RoutingPreferenceManager")) patterns.Add("RoutingPreferences");
        if (code.Contains("CopyElement")) patterns.Add("CopyElement");
        if (code.Contains("ElementTransformUtils")) patterns.Add("ElementTransform");

        return patterns;
    }

    private static List<string> ExtractCategories(string code)
    {
        var cats = new List<string>();
        if (code.Contains("OST_DuctCurves")) cats.Add("Ducts");
        if (code.Contains("OST_PipeCurves")) cats.Add("Pipes");
        if (code.Contains("OST_Conduit")) cats.Add("Conduits");
        if (code.Contains("OST_CableTray")) cats.Add("CableTrays");
        if (code.Contains("OST_DuctFitting")) cats.Add("DuctFittings");
        if (code.Contains("OST_PipeFitting")) cats.Add("PipeFittings");
        if (code.Contains("OST_MechanicalEquipment")) cats.Add("MechEquipment");
        if (code.Contains("OST_ElectricalEquipment")) cats.Add("ElecEquipment");
        if (code.Contains("OST_PlumbingFixtures")) cats.Add("PlumbingFixtures");
        if (code.Contains("OST_Sprinklers")) cats.Add("Sprinklers");
        if (code.Contains("OST_Rooms")) cats.Add("Rooms");
        if (code.Contains("OST_MEPSpaces")) cats.Add("Spaces");
        return cats;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public class CodePatternData
{
    public int TotalSuccesses { get; set; }
    public int TotalFailures { get; set; }
    public double TotalExecutionMs { get; set; }
    public Dictionary<string, int> ApiUsageCount { get; set; } = new();
    public Dictionary<string, int> CategoryQueryCount { get; set; } = new();
    public Dictionary<string, ErrorPatternEntry> ErrorPatterns { get; set; } = new();
}

public class ErrorPatternEntry
{
    public string Pattern { get; set; } = "";
    public int Count { get; set; }
    public DateTime LastSeen { get; set; }
    public string? LastError { get; set; }
    public string? LearnedFix { get; set; }
    public bool FixConfirmed { get; set; }
}
