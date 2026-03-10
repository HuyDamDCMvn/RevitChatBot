namespace RevitChatBot.Core.Skills;

public interface ISkill
{
    Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);
}

public class SkillContext
{
    public object? RevitDocument { get; set; }
    public Func<Func<object, object?>, Task<object?>>? RevitApiInvoker { get; set; }
    public Dictionary<string, object?> Extra { get; set; } = new();
}
