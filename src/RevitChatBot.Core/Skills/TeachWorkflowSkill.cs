using RevitChatBot.Core.Agent;
using RevitChatBot.Core.CodeGen;

namespace RevitChatBot.Core.Skills;

/// <summary>
/// Lets the user teach the bot a multi-step workflow by naming it and
/// specifying the ordered skill sequence. Creates a CompositeSkill and
/// registers it immediately, bypassing the CompositeSkillEngine's
/// automatic discovery (which requires minUseCount=3).
/// </summary>
[Skill("teach_workflow",
    "Teach the bot a named multi-step workflow. Specify a name and an ordered list " +
    "of skills to chain. The workflow becomes a new composite skill available immediately. " +
    "Example: 'When I say check MEP, run check_velocity then check_slope then check_insulation'.")]
[SkillParameter("name", "string",
    "Snake_case name for the workflow (e.g. 'full_mep_check').",
    isRequired: true)]
[SkillParameter("description", "string",
    "What the workflow does, in natural language.",
    isRequired: true)]
[SkillParameter("skill_sequence", "string",
    "Comma-separated ordered skill names (e.g. 'check_velocity,check_slope,check_insulation').",
    isRequired: true)]
public class TeachWorkflowSkill : ISkill
{
    public Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var name = parameters.GetValueOrDefault("name")?.ToString()?.Trim() ?? "";
        var description = parameters.GetValueOrDefault("description")?.ToString() ?? "";
        var sequenceStr = parameters.GetValueOrDefault("skill_sequence")?.ToString() ?? "";

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(sequenceStr))
            return Task.FromResult(SkillResult.Fail("Both 'name' and 'skill_sequence' are required."));

        var registry = context.Extra.GetValueOrDefault("skill_registry") as SkillRegistry;
        var executor = context.Extra.GetValueOrDefault("skill_executor") as SkillExecutor;

        if (registry is null || executor is null)
            return Task.FromResult(SkillResult.Fail(
                "SkillRegistry or SkillExecutor not available in context."));

        if (registry.GetSkill(name) is not null)
            return Task.FromResult(SkillResult.Fail($"Skill '{name}' already exists."));

        var skillNames = sequenceStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (skillNames.Length < 2)
            return Task.FromResult(SkillResult.Fail("Workflow needs at least 2 skills in the sequence."));

        var missing = skillNames.Where(s => registry.GetSkill(s) is null).ToList();
        if (missing.Count > 0)
            return Task.FromResult(SkillResult.Fail(
                $"Skills not found: {string.Join(", ", missing)}. Check skill names."));

        var chain = skillNames.Select(s => new StoredSkillCall
        {
            SkillName = s,
            Parameters = new Dictionary<string, object?>()
        }).ToList();

        var compositeSkill = new CompositeSkill(chain, executor);
        var descriptor = new SkillDescriptor
        {
            Name = name,
            Description = description + " [Composite — user-taught]",
            Parameters =
            [
                new SkillParameterDescriptor
                {
                    Name = "level",
                    Type = "string",
                    Description = "Optional level filter passed to all sub-skills",
                    IsRequired = false
                },
                new SkillParameterDescriptor
                {
                    Name = "category",
                    Type = "string",
                    Description = "Optional category filter passed to all sub-skills",
                    IsRequired = false
                }
            ]
        };

        registry.RegisterDynamic(name, compositeSkill, descriptor);

        return Task.FromResult(SkillResult.Ok(
            $"Workflow '{name}' created and registered!\n" +
            $"Sequence: {string.Join(" → ", skillNames)}\n" +
            $"Description: {description}\n" +
            $"You can now invoke it by name.",
            new { name, steps = skillNames.Length, sequence = skillNames }));
    }
}
