using System.Text.Json;

namespace RevitChatBot.MEP.Skills.Calculation;

/// <summary>
/// Isolated persistence store for Calculation Skills self-learning data.
/// Stores per-skill parameter preferences and result history for delta comparison.
/// Loaded/saved via the existing SelfLearningPersistenceManager lifecycle.
/// </summary>
public class CalculationPreferenceStore
{
    private readonly string _filePath;
    private CalcStoreData _data = new();
    private bool _dirty;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CalculationPreferenceStore(string filePath)
    {
        _filePath = filePath;
    }

    #region Parameter Preferences

    /// <summary>
    /// Save a user-provided parameter value as the preferred default for this skill+param.
    /// </summary>
    public void SavePreference(string skillName, string paramName, object? value)
    {
        var key = $"{skillName}:{paramName}";
        _data.Preferences[key] = new CalcPreference
        {
            Value = value?.ToString(),
            LastUsedUtc = DateTime.UtcNow,
            UseCount = (_data.Preferences.TryGetValue(key, out var existing)
                ? existing.UseCount : 0) + 1
        };
        _dirty = true;
    }

    /// <summary>
    /// Retrieve the saved preference for a skill parameter, or null if none saved.
    /// </summary>
    public object? GetPreference(string skillName, string paramName)
    {
        var key = $"{skillName}:{paramName}";
        return _data.Preferences.TryGetValue(key, out var pref) ? pref.Value : null;
    }

    #endregion

    #region Result History (for Delta Comparison)

    /// <summary>
    /// Save the current run's result summary. Overwrites the previous result.
    /// </summary>
    public void SaveResult(string skillName, CalcResultSummary result)
    {
        _data.LastResults[skillName] = result;
        _dirty = true;
    }

    /// <summary>
    /// Get the previous run's result summary for delta comparison.
    /// </summary>
    public CalcResultSummary? GetPreviousResult(string skillName)
    {
        return _data.LastResults.TryGetValue(skillName, out var result) ? result : null;
    }

    #endregion

    #region Persistence

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = await File.ReadAllTextAsync(_filePath, ct);
            _data = JsonSerializer.Deserialize<CalcStoreData>(json, JsonOpts) ?? new CalcStoreData();
        }
        catch
        {
            _data = new CalcStoreData();
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        if (!_dirty) return;
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir is not null) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_data, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json, ct);
            _dirty = false;
        }
        catch { /* non-critical persistence */ }
    }

    #endregion
}

#region Internal Data Models

internal class CalcStoreData
{
    public Dictionary<string, CalcPreference> Preferences { get; set; } = new();
    public Dictionary<string, CalcResultSummary> LastResults { get; set; } = new();
}

internal class CalcPreference
{
    public string? Value { get; set; }
    public DateTime LastUsedUtc { get; set; }
    public int UseCount { get; set; }
}

#endregion
