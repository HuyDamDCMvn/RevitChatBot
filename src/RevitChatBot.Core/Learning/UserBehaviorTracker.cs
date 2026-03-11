using System.Text.Json;

namespace RevitChatBot.Core.Learning;

/// <summary>
/// Learns user behavior patterns across sessions to personalize the agent's responses.
/// Tracks:
///   - Language preference (Vietnamese vs English, formal vs informal)
///   - Topic focus (which intents/categories they care about most)
///   - Workflow patterns (time-of-day patterns, sequential task patterns)
///   - Expertise level (novice asks "what is X?" vs expert asks "optimize X")
///   - Detail preference (brief vs verbose answers)
///   - Correction frequency (how often user corrects the agent → trust calibration)
/// 
/// Output: UserProfile that the agent uses to adapt tone, detail level,
/// and proactive suggestion relevance.
/// </summary>
public class UserBehaviorTracker
{
    private readonly string _filePath;
    private UserProfile _profile = new();

    public UserBehaviorTracker(string dataDir)
    {
        _filePath = Path.Combine(dataDir, "user_profile.json");
    }

    public UserProfile GetProfile() => _profile;

    public void RecordInteraction(
        string query,
        string? intent,
        string? category,
        List<string> skillsUsed,
        int stepCount,
        string? language)
    {
        _profile.InteractionCount++;
        _profile.LastActiveUtc = DateTime.UtcNow;

        if (language is not null)
            IncrementCounter(_profile.LanguageFrequency, language);

        if (intent is not null)
            IncrementCounter(_profile.IntentFrequency, intent);

        if (category is not null)
            IncrementCounter(_profile.CategoryFrequency, category);

        TrackTimeOfDay();
        AnalyzeExpertiseSignals(query, stepCount);
        TrackWorkflowSequence(intent, skillsUsed);
    }

    /// <summary>
    /// Record when user provides follow-up correction or clarification.
    /// High correction frequency = agent needs to be more careful.
    /// </summary>
    public void RecordCorrection(string originalQuery, string correction)
    {
        _profile.CorrectionCount++;
        _profile.RecentCorrections.Add(new UserCorrection
        {
            OriginalQuery = Truncate(originalQuery, 200),
            Correction = Truncate(correction, 200),
            Timestamp = DateTime.UtcNow
        });

        if (_profile.RecentCorrections.Count > 50)
            _profile.RecentCorrections.RemoveRange(0, _profile.RecentCorrections.Count - 50);
    }

    /// <summary>
    /// Record explicit user feedback (positive or negative).
    /// </summary>
    public void RecordFeedback(bool positive, string? context = null)
    {
        if (positive)
            _profile.PositiveFeedbackCount++;
        else
            _profile.NegativeFeedbackCount++;
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(_profile, JsonOpts);
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
                _profile = JsonSerializer.Deserialize<UserProfile>(json, JsonOpts) ?? new();
            }
        }
        catch { _profile = new(); }
    }

    private void TrackTimeOfDay()
    {
        var hour = DateTime.Now.Hour;
        var period = hour switch
        {
            >= 6 and < 12 => "morning",
            >= 12 and < 14 => "lunch",
            >= 14 and < 18 => "afternoon",
            >= 18 and < 22 => "evening",
            _ => "night"
        };
        IncrementCounter(_profile.ActiveTimePeriods, period);
    }

    private void AnalyzeExpertiseSignals(string query, int stepCount)
    {
        var lower = query.ToLowerInvariant();

        bool isNoviceSignal = lower.Contains("what is") || lower.Contains("how to") ||
                              lower.Contains("là gì") || lower.Contains("cách") ||
                              lower.Contains("explain") || lower.Contains("help me");

        bool isExpertSignal = lower.Contains("optimize") || lower.Contains("compare") ||
                              lower.Contains("tối ưu") || lower.Contains("so sánh") ||
                              lower.Contains("batch") || lower.Contains("correlat") ||
                              lower.Contains("trước khi") || lower.Contains("workflow");

        if (isNoviceSignal) _profile.NoviceSignalCount++;
        if (isExpertSignal) _profile.ExpertSignalCount++;

        bool wantsBrief = lower.Contains("nhanh") || lower.Contains("quick") ||
                          lower.Contains("tóm tắt") || lower.Contains("summary") ||
                          query.Length < 30;

        bool wantsDetailed = lower.Contains("chi tiết") || lower.Contains("detail") ||
                             lower.Contains("explain") || lower.Contains("giải thích") ||
                             lower.Contains("all") || lower.Contains("tất cả");

        if (wantsBrief) _profile.BriefRequestCount++;
        if (wantsDetailed) _profile.DetailedRequestCount++;
    }

    private void TrackWorkflowSequence(string? intent, List<string> skillsUsed)
    {
        if (intent is null) return;

        if (_profile.LastIntent is not null && _profile.LastIntentTimestamp.HasValue)
        {
            var gap = DateTime.UtcNow - _profile.LastIntentTimestamp.Value;
            if (gap < TimeSpan.FromMinutes(10))
            {
                var transition = $"{_profile.LastIntent}→{intent}";
                IncrementCounter(_profile.IntentTransitions, transition);
            }
        }

        _profile.LastIntent = intent;
        _profile.LastIntentTimestamp = DateTime.UtcNow;
    }

    private static void IncrementCounter(Dictionary<string, int> dict, string key)
    {
        dict.TryGetValue(key, out var count);
        dict[key] = count + 1;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public class UserProfile
{
    public int InteractionCount { get; set; }
    public DateTime LastActiveUtc { get; set; }
    public Dictionary<string, int> LanguageFrequency { get; set; } = new();
    public Dictionary<string, int> IntentFrequency { get; set; } = new();
    public Dictionary<string, int> CategoryFrequency { get; set; } = new();
    public Dictionary<string, int> ActiveTimePeriods { get; set; } = new();
    public Dictionary<string, int> IntentTransitions { get; set; } = new();

    public int NoviceSignalCount { get; set; }
    public int ExpertSignalCount { get; set; }
    public int BriefRequestCount { get; set; }
    public int DetailedRequestCount { get; set; }
    public int CorrectionCount { get; set; }
    public int PositiveFeedbackCount { get; set; }
    public int NegativeFeedbackCount { get; set; }

    public string? LastIntent { get; set; }
    public DateTime? LastIntentTimestamp { get; set; }
    public List<UserCorrection> RecentCorrections { get; set; } = [];

    /// <summary>
    /// Derived: estimated expertise level based on query patterns.
    /// </summary>
    public string EstimatedExpertise
    {
        get
        {
            if (InteractionCount < 5) return "unknown";
            var ratio = ExpertSignalCount > 0
                ? (double)ExpertSignalCount / (NoviceSignalCount + ExpertSignalCount)
                : 0;
            return ratio switch
            {
                > 0.6 => "expert",
                > 0.3 => "intermediate",
                _ => "beginner"
            };
        }
    }

    /// <summary>
    /// Derived: preferred response detail level.
    /// </summary>
    public string? PreferredDetailLevel
    {
        get
        {
            if (BriefRequestCount + DetailedRequestCount < 3) return null;
            var briefRatio = (double)BriefRequestCount / (BriefRequestCount + DetailedRequestCount);
            return briefRatio switch
            {
                > 0.65 => "brief",
                < 0.35 => "detailed",
                _ => null
            };
        }
    }

    /// <summary>
    /// Derived: preferred language.
    /// </summary>
    public string? PreferredLanguage
    {
        get
        {
            if (LanguageFrequency.Count == 0) return null;
            return LanguageFrequency.MaxBy(kv => kv.Value).Key;
        }
    }

    /// <summary>
    /// Derived: most common intents.
    /// </summary>
    public Dictionary<string, int> TopIntents =>
        IntentFrequency.OrderByDescending(kv => kv.Value)
            .Take(5).ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>
    /// Derived: predicted next intent based on transition probabilities.
    /// Given a current intent, what is the user likely to ask next?
    /// </summary>
    public string? PredictNextIntent(string currentIntent)
    {
        var prefix = $"{currentIntent}→";
        var candidates = IntentTransitions
            .Where(kv => kv.Key.StartsWith(prefix))
            .OrderByDescending(kv => kv.Value)
            .Take(1)
            .ToList();

        if (candidates.Count == 0) return null;
        return candidates[0].Key[prefix.Length..];
    }

    /// <summary>
    /// Agent trust score: high corrections = lower trust = more careful responses.
    /// </summary>
    public double TrustScore
    {
        get
        {
            if (InteractionCount < 3) return 0.5;
            var correctionRate = (double)CorrectionCount / InteractionCount;
            return Math.Max(0.1, 1.0 - correctionRate * 2);
        }
    }
}

public class UserCorrection
{
    public string OriginalQuery { get; set; } = "";
    public string Correction { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
