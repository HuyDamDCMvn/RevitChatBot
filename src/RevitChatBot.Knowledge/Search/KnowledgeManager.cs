using RevitChatBot.Knowledge.Documents;
using RevitChatBot.Knowledge.Embeddings;
using RevitChatBot.Knowledge.VectorStore;

namespace RevitChatBot.Knowledge.Search;

/// <summary>
/// Orchestrates document loading, embedding, and search for RAG.
/// </summary>
public class KnowledgeManager
{
    private readonly IEmbeddingService _embedding;
    private readonly IVectorStore _vectorStore;
    private readonly List<IDocumentLoader> _loaders;
    private readonly string _persistPath;

    public KnowledgeManager(
        IEmbeddingService embedding,
        IVectorStore vectorStore,
        string persistPath,
        IEnumerable<IDocumentLoader>? loaders = null)
    {
        _embedding = embedding;
        _vectorStore = vectorStore;
        _persistPath = persistPath;
        _loaders = loaders?.ToList() ?? [new TextDocumentLoader(), new JsonDocumentLoader(), new PdfDocumentLoader()];
    }

    public async Task IndexDirectoryAsync(string directoryPath, CancellationToken ct = default)
    {
        if (!Directory.Exists(directoryPath)) return;

        var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            await IndexFileAsync(file, ct);
        }

        await _vectorStore.SaveAsync(_persistPath, ct);
    }

    public async Task IndexFileAsync(string filePath, CancellationToken ct = default)
    {
        var loader = _loaders.FirstOrDefault(l => l.CanHandle(filePath));
        if (loader is null) return;

        var chunks = await loader.LoadAsync(filePath, ct);
        if (chunks.Count == 0) return;

        var texts = chunks.Select(c => c.Content).ToList();
        var embeddings = await _embedding.GetEmbeddingsAsync(texts, ct);

        var entries = chunks.Zip(embeddings, (chunk, emb) => new VectorEntry
        {
            Id = chunk.Id,
            Text = chunk.Content,
            Embedding = emb,
            Metadata = new Dictionary<string, string>(chunk.Metadata)
            {
                ["source"] = chunk.Source,
                ["category"] = chunk.Category,
                ["chunk_index"] = chunk.ChunkIndex.ToString()
            }
        });

        await _vectorStore.AddRangeAsync(entries, ct);
    }

    public async Task<List<SearchResult>> SearchAsync(string query, int topK = 5, CancellationToken ct = default)
    {
        var queryEmbedding = await _embedding.GetEmbeddingAsync(query, ct);
        return await _vectorStore.SearchAsync(queryEmbedding, topK, ct);
    }

    public async Task LoadIndexAsync(CancellationToken ct = default)
    {
        await _vectorStore.LoadAsync(_persistPath, ct);
    }

    public async Task<int> GetIndexedCountAsync(CancellationToken ct = default)
    {
        return await _vectorStore.CountAsync(ct);
    }
}
