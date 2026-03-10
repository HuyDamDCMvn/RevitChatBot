namespace RevitChatBot.Core.Skills;

public class SkillDescriptor
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<SkillParameterDescriptor> Parameters { get; set; } = new();
}

public class SkillParameterDescriptor
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public string Description { get; set; } = string.Empty;
    public bool IsRequired { get; set; } = true;
    public List<string>? AllowedValues { get; set; }
}
