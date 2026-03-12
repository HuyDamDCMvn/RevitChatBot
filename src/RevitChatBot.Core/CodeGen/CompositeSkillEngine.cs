using System.Text.Json;
using RevitChatBot.Core.Agent;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.Core.CodeGen;

/// <summary>
/// Discovers repeated multi-skill patterns from PlanReplayStore and
/// automatically promotes them as composite (macro) skills.
/// A composite skill chains multiple existing skills sequentially.
/// </summary>
public class CompositeSkillEngine
{
    private readonly PlanReplayStore _planStore;
    private readonly SkillRegistry _skillRegistry;
    private readonly SkillExecutor _executor;

    public CompositeSkillEngine(
        PlanReplayStore planStore,
        SkillRegistry skillRegistry,
        SkillExecutor skillExecutor)
    {
        _planStore = planStore;
        _skillRegistry = skillRegistry;
        _executor = skillExecutor;
    }

    /// <summary>
    /// Scan the plan store for repeated skill chain patterns that exceed minUseCount.
    /// Returns candidates ranked by frequency.
    /// </summary>
    public List<CompositeSkillCandidate> DiscoverCandidates(int minUseCount = 3)
    {
        var plans = _planStore.GetAllPlans();
        if (plans.Count == 0) return [];

        var patterns = plans
            .Where(p => p.SkillChain.Count >= 2)
            .GroupBy(p => string.Join("→", p.SkillChain.Select(s => s.SkillName)))
            .Where(g => g.Sum(p => p.UseCount) >= minUseCount)
            .Select(g =>
            {
                var representative = g.OrderByDescending(p => p.UseCount).First();
                return new CompositeSkillCandidate
                {
                    SkillChain = representative.SkillChain,
                    UsageCount = g.Sum(p => p.UseCount),
                    TypicalGoals = g.Select(p => p.Goal).Distinct().Take(3).ToList(),
                    SuggestedName = GenerateName(representative.SkillChain),
                    SuggestedDescription = GenerateDescription(
                        representative.SkillChain, g.Select(p => p.Goal))
                };
            })
            .Where(c => _skillRegistry.GetSkill(c.SuggestedName) == null)
            .OrderByDescending(c => c.UsageCount)
            .ToList();

        return patterns;
    }

    /// <summary>
    /// Promote a candidate to a registered composite skill.
    /// </summary>
    public bool PromoteToCompositeSkill(CompositeSkillCandidate candidate)
    {
        if (_skillRegistry.GetSkill(candidate.SuggestedName) != null)
            return false;

        var skill = new CompositeSkill(candidate.SkillChain, _executor);
        var descriptor = new SkillDescriptor
        {
            Name = candidate.SuggestedName,
            Description = candidate.SuggestedDescription + " [Composite — auto-discovered]",
            Parameters = MergeParameters(candidate.SkillChain)
        };

        _skillRegistry.RegisterDynamic(candidate.SuggestedName, skill, descriptor);
        return true;
    }

    private static readonly List<(string Name, string Description, string Chain)> BuiltInRecipes =
    [
        ("filter_then_modify", "Filter elements then batch-modify a parameter on matched results",
            "advanced_filter → batch_modify(source=element_ids)"),
        ("filter_then_map_table", "Filter elements then map data table values onto matched results",
            "advanced_filter → map_data_table(source=element_ids)"),
        ("export_edit_reimport", "Export elements to CSV, user edits externally, then reimport changes",
            "export_to_csv → batch_update_from_csv"),
        ("selection_export", "Export currently selected elements to CSV",
            "export_to_csv(source=selected)"),
        ("selection_modify", "Batch-modify parameters on currently selected elements",
            "batch_modify(source=selected)"),
        ("selection_highlight", "Highlight currently selected elements in 3D view",
            "highlight_elements(source=selected)"),
        ("selection_color", "Override color of currently selected elements",
            "override_element_color(source=selected)"),
        ("filter_then_color", "Filter elements then override their color in the active view",
            "advanced_filter → override_element_color"),
    ];

    /// <summary>
    /// Get a summary of composite skills available, for LLM context injection.
    /// Includes both auto-discovered patterns and built-in recipe suggestions.
    /// </summary>
    public string GetCompositeSkillsSummary()
    {
        var lines = new List<string>();

        var candidates = DiscoverCandidates(minUseCount: 2);
        if (candidates.Count > 0)
        {
            lines.Add("[composite_skill_patterns] — Auto-discovered:");
            foreach (var c in candidates.Take(10))
            {
                lines.Add($"  - {c.SuggestedName}: {c.SuggestedDescription} (used {c.UsageCount}x)");
                lines.Add($"    chain: {string.Join(" → ", c.SkillChain.Select(s => s.SkillName))}");
            }
        }

        lines.Add("[built_in_workflow_recipes] — You can combine these skills:");
        foreach (var (name, desc, chain) in BuiltInRecipes)
            lines.Add($"  - {name}: {desc} → {chain}");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Promote a workflow template (from SkillDiscoveryAgent) to a composite skill.
    /// Only registers if all skills in the sequence exist and the name isn't taken.
    /// </summary>
    public bool PromoteFromWorkflow(WorkflowTemplate workflow)
    {
        if (string.IsNullOrEmpty(workflow.Name) || workflow.SkillSequence.Count < 2)
            return false;

        var skillName = $"composite_{workflow.Name}";
        if (_skillRegistry.GetSkill(skillName) != null)
            return false;

        var chain = workflow.SkillSequence
            .Select(name => new StoredSkillCall { SkillName = name, Parameters = new() })
            .ToList();

        if (chain.Any(s => _skillRegistry.GetSkill(s.SkillName) == null))
            return false;

        var skill = new CompositeSkill(chain, _executor);
        var descriptor = new SkillDescriptor
        {
            Name = skillName,
            Description = (workflow.Description ?? $"Runs {string.Join(", ", workflow.SkillSequence)}")
                + " [Composite — LLM-discovered]",
            Parameters = MergeParameters(chain)
        };

        _skillRegistry.RegisterDynamic(skillName, skill, descriptor);
        return true;
    }

    private static string GenerateName(List<StoredSkillCall> chain)
    {
        var parts = chain.Select(s =>
        {
            var name = s.SkillName;
            if (name.StartsWith("check_")) return name.Replace("check_", "chk_");
            if (name.StartsWith("query_")) return name.Replace("query_", "q_");
            if (name.Length > 15) return name[..15];
            return name;
        });
        return "composite_" + string.Join("_", parts);
    }

    private static string GenerateDescription(
        List<StoredSkillCall> chain, IEnumerable<string> goals)
    {
        var skillNames = string.Join(", ", chain.Select(s => s.SkillName));
        var sampleGoal = goals.FirstOrDefault() ?? "";
        return $"Runs {chain.Count} skills in sequence ({skillNames}). " +
               $"Typical use: \"{Truncate(sampleGoal, 80)}\"";
    }

    private static List<SkillParameterDescriptor> MergeParameters(List<StoredSkillCall> chain)
    {
        var seen = new HashSet<string>();
        var merged = new List<SkillParameterDescriptor>();

        merged.Add(new SkillParameterDescriptor
        {
            Name = "level_name",
            Type = "string",
            Description = "Building level to scope the composite operation",
            IsRequired = false
        });

        merged.Add(new SkillParameterDescriptor
        {
            Name = "category",
            Type = "string",
            Description = "MEP category filter",
            IsRequired = false,
            AllowedValues = ["duct", "pipe", "conduit", "cable_tray", "equipment", "all"]
        });

        return merged;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}

/// <summary>
/// A skill that chains multiple existing skills in sequence.
/// Parameters from the composite call are forwarded to each sub-skill.
/// </summary>
public class CompositeSkill : ISkill
{
    private readonly List<StoredSkillCall> _chain;
    private readonly SkillExecutor _executor;

    public CompositeSkill(List<StoredSkillCall> chain, SkillExecutor executor)
    {
        _chain = chain;
        _executor = executor;
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var results = new List<string>();

        for (int i = 0; i < _chain.Count; i++)
        {
            var step = _chain[i];

            var mergedParams = new Dictionary<string, object?>(step.Parameters);
            foreach (var (key, value) in parameters)
            {
                if (value != null)
                    mergedParams[key] = value;
            }

            var result = await _executor.ExecuteAsync(step.SkillName, mergedParams, cancellationToken);
            results.Add($"**Step {i + 1} [{step.SkillName}]**: {result.Message}");

            if (!result.Success)
                return SkillResult.Fail(
                    $"Composite failed at step {i + 1} ({step.SkillName}): {result.Message}");
        }

        return SkillResult.Ok(string.Join("\n\n", results),
            new { compositeSteps = _chain.Count });
    }
}

public class CompositeSkillCandidate
{
    public List<StoredSkillCall> SkillChain { get; set; } = [];
    public int UsageCount { get; set; }
    public List<string> TypicalGoals { get; set; } = [];
    public string SuggestedName { get; set; } = "";
    public string SuggestedDescription { get; set; } = "";
}
