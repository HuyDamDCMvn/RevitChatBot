using RevitChatBot.Core.Memory;

namespace RevitChatBot.Core.Learning;

/// <summary>
/// Discovers correlations between skills that go beyond simple sequential chains.
/// While InteractionRecorder tracks linear chains (A→B→C), this analyzer finds:
///
///   1. CO-OCCURRENCE: Skills that tend to appear in the same plan regardless of order
///      Example: "check_clearance" and "highlight_elements" co-occur in 90% of plans
///
///   2. CONDITIONAL PATTERNS: "When skill A returns warnings, skill B usually follows"
///      Example: "After check_velocity shows issues, user often runs query_duct_sizing"
///
///   3. FAILURE RECOVERY: "When skill A fails, which skill B succeeds as fallback?"
///      Example: "If query_elements fails, execute_revit_code usually succeeds"
///
///   4. PERFORMANCE CORRELATION: "Skill A is slow when preceded by skill B"
///      Helps the agent optimize execution order.
///
///   5. MUTUAL EXCLUSION: Skills that are never used together (conflict detection)
///
/// These correlations are richer than simple Markov chains because they capture
/// semantic relationships, not just transition probabilities.
/// </summary>
public class CrossSkillCorrelator
{
    private readonly Dictionary<string, SkillStats> _skillStats = new();
    private readonly Dictionary<string, int> _pairCoOccurrence = new();
    private readonly Dictionary<string, int> _orderedTransitions = new();
    private readonly Dictionary<string, int> _failureRecoveries = new();
    private int _totalSequences;

    /// <summary>
    /// Record a complete skill sequence from a single plan execution.
    /// </summary>
    public void RecordSkillSequence(
        List<string> skillsUsed, bool planSucceeded, int totalSteps)
    {
        if (skillsUsed.Count == 0) return;
        _totalSequences++;

        var uniqueSkills = skillsUsed.Distinct().ToList();

        // Track co-occurrence: every pair of skills in this plan
        for (int i = 0; i < uniqueSkills.Count; i++)
        {
            for (int j = i + 1; j < uniqueSkills.Count; j++)
            {
                var pairKey = MakePairKey(uniqueSkills[i], uniqueSkills[j]);
                _pairCoOccurrence.TryGetValue(pairKey, out var count);
                _pairCoOccurrence[pairKey] = count + 1;
            }
        }

        // Track ordered transitions: A→B pairs in sequence
        for (int i = 0; i < skillsUsed.Count - 1; i++)
        {
            var transKey = $"{skillsUsed[i]}→{skillsUsed[i + 1]}";
            _orderedTransitions.TryGetValue(transKey, out var transCount);
            _orderedTransitions[transKey] = transCount + 1;
        }

        // Track individual skill stats
        foreach (var skill in skillsUsed)
        {
            if (!_skillStats.TryGetValue(skill, out var stats))
            {
                stats = new SkillStats { Name = skill };
                _skillStats[skill] = stats;
            }
            stats.TotalUses++;
            if (planSucceeded) stats.SuccessfulPlanCount++;
        }
    }

    /// <summary>
    /// Record a failure recovery: skill A failed, then skill B was tried.
    /// </summary>
    public void RecordFailureRecovery(string failedSkill, string recoverySkill, bool recovered)
    {
        if (!recovered) return;
        var key = $"{failedSkill}⇒{recoverySkill}";
        _failureRecoveries.TryGetValue(key, out var count);
        _failureRecoveries[key] = count + 1;
    }

    /// <summary>
    /// Ingest performance data from SessionAnalytics.
    /// </summary>
    public void UpdatePerformanceStats(SessionAnalytics analytics)
    {
        foreach (var (name, stat) in analytics.SkillStats)
        {
            if (!_skillStats.TryGetValue(name, out var existing))
            {
                existing = new SkillStats { Name = name };
                _skillStats[name] = existing;
            }
            existing.AvgDurationMs = stat.AvgDurationMs;
            existing.SuccessRate = stat.SuccessRate;
        }
    }

    /// <summary>
    /// Get top correlated skill pairs, sorted by correlation strength.
    /// </summary>
    public List<SkillCorrelation> GetTopCorrelations(int maxCount = 10)
    {
        if (_totalSequences < 2) return [];

        var correlations = new List<SkillCorrelation>();

        foreach (var (pairKey, coCount) in _pairCoOccurrence)
        {
            var (skillA, skillB) = ParsePairKey(pairKey);
            var sA = _skillStats.GetValueOrDefault(skillA);
            var sB = _skillStats.GetValueOrDefault(skillB);
            if (sA is null || sB is null) continue;

            // Jaccard-like: co-occurrence / (A-uses + B-uses - co-occurrence)
            var union = sA.TotalUses + sB.TotalUses - coCount;
            var strength = union > 0 ? (double)coCount / union : 0;

            // Check directionality
            var abCount = _orderedTransitions.GetValueOrDefault($"{skillA}→{skillB}");
            var baCount = _orderedTransitions.GetValueOrDefault($"{skillB}→{skillA}");
            bool orderMatters = Math.Abs(abCount - baCount) > Math.Max(abCount, baCount) * 0.4;
            bool aFirst = abCount >= baCount;

            if (strength < 0.15) continue;

            correlations.Add(new SkillCorrelation
            {
                SkillA = aFirst ? skillA : skillB,
                SkillB = aFirst ? skillB : skillA,
                CoOccurrence = coCount,
                CorrelationStrength = strength,
                OrderMatters = orderMatters,
                ABTransitionCount = aFirst ? abCount : baCount,
                BATransitionCount = aFirst ? baCount : abCount
            });
        }

        return correlations
            .OrderByDescending(c => c.CorrelationStrength)
            .Take(maxCount)
            .ToList();
    }

    /// <summary>
    /// Find best fallback skill when the given skill fails.
    /// </summary>
    public string? GetBestFallback(string failedSkill)
    {
        var candidates = _failureRecoveries
            .Where(kv => kv.Key.StartsWith($"{failedSkill}⇒"))
            .OrderByDescending(kv => kv.Value)
            .FirstOrDefault();

        if (candidates.Key is null) return null;
        return candidates.Key.Split('⇒').LastOrDefault();
    }

    /// <summary>
    /// Get skill performance ranking (sorted by success rate * usage).
    /// Helps the agent prefer reliable skills.
    /// </summary>
    public List<SkillStats> GetPerformanceRanking()
    {
        return _skillStats.Values
            .OrderByDescending(s => s.SuccessRate * Math.Log2(1 + s.TotalUses))
            .ToList();
    }

    /// <summary>
    /// Detect mutual exclusion: skills that never co-occur despite both being common.
    /// </summary>
    public List<(string SkillA, string SkillB)> GetMutuallyExclusiveSkills(int minUses = 5)
    {
        var commonSkills = _skillStats.Values
            .Where(s => s.TotalUses >= minUses)
            .Select(s => s.Name)
            .ToList();

        var exclusive = new List<(string, string)>();
        for (int i = 0; i < commonSkills.Count; i++)
        {
            for (int j = i + 1; j < commonSkills.Count; j++)
            {
                var pairKey = MakePairKey(commonSkills[i], commonSkills[j]);
                if (!_pairCoOccurrence.ContainsKey(pairKey))
                    exclusive.Add((commonSkills[i], commonSkills[j]));
            }
        }
        return exclusive;
    }

    private static string MakePairKey(string a, string b)
    {
        return string.Compare(a, b, StringComparison.Ordinal) <= 0
            ? $"{a}|{b}" : $"{b}|{a}";
    }

    private static (string A, string B) ParsePairKey(string key)
    {
        var parts = key.Split('|');
        return parts.Length == 2 ? (parts[0], parts[1]) : ("", "");
    }
}

public class SkillCorrelation
{
    public string SkillA { get; set; } = "";
    public string SkillB { get; set; } = "";
    public int CoOccurrence { get; set; }
    public double CorrelationStrength { get; set; }
    public bool OrderMatters { get; set; }
    public int ABTransitionCount { get; set; }
    public int BATransitionCount { get; set; }
}

public class SkillStats
{
    public string Name { get; set; } = "";
    public int TotalUses { get; set; }
    public int SuccessfulPlanCount { get; set; }
    public double AvgDurationMs { get; set; }
    public double SuccessRate { get; set; }
}
