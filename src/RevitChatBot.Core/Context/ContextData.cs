namespace RevitChatBot.Core.Context;

public class ContextData
{
    public Dictionary<string, string> Entries { get; set; } = new();

    public void Add(string key, string value)
    {
        Entries[key] = value;
    }

    public void Merge(ContextData other)
    {
        foreach (var (key, value) in other.Entries)
            Entries[key] = value;
    }

    public static ContextData Empty => new();
}
