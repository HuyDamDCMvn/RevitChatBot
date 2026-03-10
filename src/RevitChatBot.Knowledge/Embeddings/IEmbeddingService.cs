namespace RevitChatBot.Knowledge.Embeddings;

public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);
    Task<List<float[]>> GetEmbeddingsAsync(List<string> texts, CancellationToken ct = default);
}
