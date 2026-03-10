using System.Reflection;

namespace RevitChatBot.Core.Skills;

public class SkillRegistry
{
    private readonly Dictionary<string, ISkill> _skills = new();
    private readonly Dictionary<string, SkillDescriptor> _descriptors = new();

    public void Register(ISkill skill)
    {
        var type = skill.GetType();
        var attr = type.GetCustomAttribute<SkillAttribute>()
            ?? throw new InvalidOperationException(
                $"Skill {type.Name} must have [Skill] attribute.");

        var paramAttrs = type.GetCustomAttributes<SkillParameterAttribute>();

        var descriptor = new SkillDescriptor
        {
            Name = attr.Name,
            Description = attr.Description,
            Parameters = paramAttrs.Select(p => new SkillParameterDescriptor
            {
                Name = p.Name,
                Type = p.Type,
                Description = p.Description,
                IsRequired = p.IsRequired,
                AllowedValues = p.AllowedValues?.ToList()
            }).ToList()
        };

        _skills[attr.Name] = skill;
        _descriptors[attr.Name] = descriptor;
    }

    public void RegisterFromAssembly(Assembly assembly)
    {
        var skillTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(ISkill).IsAssignableFrom(t)
                        && t.GetCustomAttribute<SkillAttribute>() is not null);

        foreach (var type in skillTypes)
        {
            var skill = (ISkill)Activator.CreateInstance(type)!;
            Register(skill);
        }
    }

    public ISkill? GetSkill(string name) =>
        _skills.GetValueOrDefault(name);

    public SkillDescriptor? GetDescriptor(string name) =>
        _descriptors.GetValueOrDefault(name);

    public IEnumerable<SkillDescriptor> GetAllDescriptors() =>
        _descriptors.Values;

    public IEnumerable<string> GetAllSkillNames() =>
        _skills.Keys;

    public int Count => _skills.Count;
}
