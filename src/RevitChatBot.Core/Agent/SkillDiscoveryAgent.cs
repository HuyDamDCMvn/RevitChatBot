using System.Text.Json.Nodes;
using RevitChatBot.Core.LLM;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.Core.Agent;

/// <summary>
/// Proactively discovers new composite workflow opportunities by analyzing
/// existing skills and past interaction patterns. Runs during idle time.
/// </summary>
public class SkillDiscoveryAgent
{
    private readonly IOllamaService _ollama;
    private readonly SkillRegistry _registry;
    private readonly PlanReplayStore _planStore;

    public SkillDiscoveryAgent(
        IOllamaService ollama,
        SkillRegistry registry,
        PlanReplayStore planStore)
    {
        _ollama = ollama;
        _registry = registry;
        _planStore = planStore;
    }

    /// <summary>
    /// Ask the LLM to suggest new composite workflows based on available skills.
    /// Returns workflow templates that can be registered as composite skills.
    /// </summary>
    public async Task<List<WorkflowTemplate>> DiscoverWorkflows(CancellationToken ct = default)
    {
        var skillDescriptions = string.Join("\n", _registry.GetAllDescriptors()
            .Select(d => $"  - {d.Name}: {d.Description}"));

        var existingWorkflows = _planStore.GetAllPlans()
            .Where(p => p.SkillChain.Count >= 2)
            .Select(p => string.Join(" → ", p.SkillChain.Select(s => s.SkillName)))
            .Distinct()
            .Take(15);

        var prompt = $"""
            You are an MEP engineering automation expert.
            
            Available skills:
            {skillDescriptions}
            
            Previously successful workflows:
            {string.Join("\n", existingWorkflows.Select(w => $"  - {w}"))}
            
            Suggest 5 NEW useful composite workflows that combine 2-5 skills.
            Focus on common MEP tasks:
            - Full system QA/QC checks
            - Pre-coordination review
            - Design validation
            - Model cleanup / audit
            - Handover preparation
            - System-specific checks (HVAC, plumbing, electrical)
            
            Each workflow needs: name (snake_case), description, ordered skill sequence, and when to use it.
            """;

        try
        {
            var result = await _ollama.GenerateAsync(prompt,
                formatJson: """
                {
                    "type": "object",
                    "properties": {
                        "workflows": {
                            "type": "array",
                            "items": {
                                "type": "object",
                                "properties": {
                                    "name": {"type": "string"},
                                    "description": {"type": "string"},
                                    "skill_sequence": {"type": "array", "items": {"type": "string"}},
                                    "use_case": {"type": "string"}
                                },
                                "required": ["name", "description", "skill_sequence"]
                            }
                        }
                    },
                    "required": ["workflows"]
                }
                """,
                temperature: 0.4,
                numCtx: 4096,
                cancellationToken: ct);

            return ParseWorkflows(result);
        }
        catch
        {
            return [];
        }
    }

    private List<WorkflowTemplate> ParseWorkflows(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            var workflows = node?["workflows"]?.AsArray();
            if (workflows == null) return [];

            return workflows
                .Where(w => w != null)
                .Select(w => new WorkflowTemplate
                {
                    Name = w!["name"]?.GetValue<string>() ?? "",
                    Description = w["description"]?.GetValue<string>() ?? "",
                    SkillSequence = w["skill_sequence"]?.AsArray()
                        .Select(s => s?.GetValue<string>() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList() ?? [],
                    UseCase = w["use_case"]?.GetValue<string>()
                })
                .Where(t => t.SkillSequence.Count >= 2 && !string.IsNullOrEmpty(t.Name))
                .Where(t => t.SkillSequence.All(s =>
                    _registry.GetSkill(s) != null))
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}

public class WorkflowTemplate
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> SkillSequence { get; set; } = [];
    public string? UseCase { get; set; }
}
