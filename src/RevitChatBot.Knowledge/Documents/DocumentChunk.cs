namespace RevitChatBot.Knowledge.Documents;

public class DocumentChunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Content { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}
