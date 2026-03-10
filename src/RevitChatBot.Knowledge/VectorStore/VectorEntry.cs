namespace RevitChatBot.Knowledge.VectorStore;

public class VectorEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Text { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class SearchResult
{
    public VectorEntry Entry { get; set; } = null!;
    public double Score { get; set; }
}
