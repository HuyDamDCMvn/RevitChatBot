namespace RevitChatBot.Visualization.Learning;

/// <summary>
/// Analyzes visualization patterns to auto-compose "visual workflows" --
/// skill chains that include visualization steps. These are composite skills
/// that the agent can call as a single action, e.g.:
///
///   "visual_qaqc_clearance" = check_clearance → highlight_elements(critical) → 
///                             check_insulation → highlight_elements(warning)
///
///   "visual_clash_review" = detect_clashes → visualize_clashes → avoid_clash(analyze)
///
/// This is the key innovation: the agent doesn't just learn WHAT to check,
/// it learns HOW TO SHOW the results. The visualization becomes part of the workflow.
///
/// Self-learning loop:
///   1. Agent discovers pattern: "every time user asks for QA/QC, I run check_X then highlight"
///   2. VisualWorkflowComposer detects this pattern
///   3. Creates a composite skill with visualization baked in
///   4. Next time, agent calls the composite directly → faster, more consistent
///   5. User behavior (clear quickly? ask follow-up?) refines the pattern
/// </summary>
public class VisualWorkflowComposer
{
    private readonly VisualFeedbackLearner _learner;
    private readonly List<VisualWorkflowTemplate> _templates = [];
    private readonly string _dataPath;

    public VisualWorkflowComposer(VisualFeedbackLearner learner, string dataDir)
    {
        _learner = learner;
        _dataPath = Path.Combine(dataDir, "visual_workflows.json");
    }

    public Task LoadAsync(CancellationToken ct = default)
    {
        DiscoverVisualWorkflows();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Analyze learned patterns and generate workflow templates that
    /// combine check/query skills with visualization steps.
    /// </summary>
    public List<VisualWorkflowTemplate> DiscoverVisualWorkflows()
    {
        var context = _learner.GetLearnedPatternsContext();
        if (string.IsNullOrEmpty(context)) return [];

        var workflows = new List<VisualWorkflowTemplate>();

        var checkSkills = new[]
        {
            ("check_clearance", "clearance_check", "Clearance violations"),
            ("check_insulation", "insulation_check", "Missing insulation"),
            ("check_fire_damper", "fire_damper_check", "Fire damper issues"),
            ("check_velocity", "velocity_check", "Velocity exceedances"),
            ("check_slope", "slope_check", "Slope violations"),
            ("check_connection", "connection_check", "Disconnected elements"),
            ("detect_clashes", "clash_detection", "Clash pairs"),
            ("model_audit", "model_audit", "Model audit findings"),
        };

        foreach (var (skillName, tag, description) in checkSkills)
        {
            var recommendation = _learner.GetRecommendation(skillName);
            if (recommendation is not { ShouldVisualize: true, Confidence: > 0.3 }) continue;

            var workflow = new VisualWorkflowTemplate
            {
                Name = $"visual_{tag}",
                Description = $"{description} with automatic 3D highlighting",
                Steps =
                [
                    new VisualWorkflowStep
                    {
                        SkillName = "clear_visualization",
                        ParameterTemplate = new() { ["tag"] = tag },
                        Purpose = "Clear previous highlights"
                    },
                    new VisualWorkflowStep
                    {
                        SkillName = skillName,
                        ParameterTemplate = new(),
                        Purpose = $"Run {skillName}",
                        CaptureElementIds = true
                    },
                    new VisualWorkflowStep
                    {
                        SkillName = recommendation.VisualizationAction,
                        ParameterTemplate = new()
                        {
                            ["severity"] = recommendation.RecommendedSeverity,
                            ["tag"] = tag
                        },
                        Purpose = "Highlight results in 3D view",
                        UseCapturedElementIds = true
                    }
                ],
                Confidence = recommendation.Confidence,
                LearnedFromCount = (int)(recommendation.Confidence * 10)
            };

            workflows.Add(workflow);
        }

        _templates.Clear();
        _templates.AddRange(workflows);
        return workflows;
    }

    /// <summary>
    /// Get a prompt context block describing available visual workflows
    /// for the agent to call or reference.
    /// </summary>
    public string GetVisualWorkflowsContext()
    {
        if (_templates.Count == 0)
            DiscoverVisualWorkflows();

        if (_templates.Count == 0) return "";

        var lines = new List<string> { "[visual_workflows] (auto-composed from learned patterns)" };
        foreach (var wf in _templates.Take(8))
        {
            var steps = string.Join(" → ", wf.Steps.Select(s => s.SkillName));
            lines.Add($"  - {wf.Name}: {wf.Description}");
            lines.Add($"    steps: {steps} (confidence: {wf.Confidence:F2})");
        }

        lines.Add("  Tip: Instead of calling skills individually, mention a visual workflow name " +
                   "and the agent will execute the full sequence including 3D highlighting.");

        return string.Join("\n", lines);
    }
}

public class VisualWorkflowTemplate
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<VisualWorkflowStep> Steps { get; set; } = [];
    public double Confidence { get; set; }
    public int LearnedFromCount { get; set; }
}

public class VisualWorkflowStep
{
    public string SkillName { get; set; } = "";
    public Dictionary<string, object?> ParameterTemplate { get; set; } = new();
    public string Purpose { get; set; } = "";
    public bool CaptureElementIds { get; set; }
    public bool UseCapturedElementIds { get; set; }
}
