namespace RevitChatBot.Knowledge.Documents;

public interface IDocumentLoader
{
    Task<List<DocumentChunk>> LoadAsync(string sourcePath, CancellationToken ct = default);
    bool CanHandle(string sourcePath);
}
