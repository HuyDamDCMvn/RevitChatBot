namespace RevitChatBot.Core.Skills;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class SkillAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }

    public SkillAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class SkillParameterAttribute : Attribute
{
    public string Name { get; }
    public string Type { get; }
    public string Description { get; }
    public bool IsRequired { get; }
    public string[]? AllowedValues { get; }

    public SkillParameterAttribute(
        string name,
        string type,
        string description,
        bool isRequired = true,
        string[]? allowedValues = null)
    {
        Name = name;
        Type = type;
        Description = description;
        IsRequired = isRequired;
        AllowedValues = allowedValues;
    }
}
