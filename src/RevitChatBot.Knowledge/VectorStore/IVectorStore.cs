namespace RevitChatBot.Knowledge.VectorStore;

public interface IVectorStore
{
    Task AddAsync(VectorEntry entry, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<VectorEntry> entries, CancellationToken ct = default);
    Task<List<SearchResult>> SearchAsync(float[] queryEmbedding, int topK = 5, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
    Task SaveAsync(string path, CancellationToken ct = default);
    Task LoadAsync(string path, CancellationToken ct = default);
}
