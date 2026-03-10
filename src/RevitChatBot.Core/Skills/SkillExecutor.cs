namespace RevitChatBot.Core.Skills;

public class SkillExecutor
{
    private readonly SkillRegistry _registry;
    private readonly SkillContext _context;

    public SkillExecutor(SkillRegistry registry, SkillContext context)
    {
        _registry = registry;
        _context = context;
    }

    public async Task<SkillResult> ExecuteAsync(
        string skillName,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var skill = _registry.GetSkill(skillName);
        if (skill is null)
            return SkillResult.Fail($"Skill '{skillName}' not found.");

        try
        {
            return await skill.ExecuteAsync(_context, parameters, cancellationToken);
        }
        catch (Exception ex)
        {
            return SkillResult.Fail(
                $"Skill '{skillName}' failed: {ex.Message}",
                ex.ToString());
        }
    }
}
