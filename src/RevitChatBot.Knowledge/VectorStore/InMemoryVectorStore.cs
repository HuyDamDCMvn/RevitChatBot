using System.Text.Json;

namespace RevitChatBot.Knowledge.VectorStore;

/// <summary>
/// Simple in-memory vector store using cosine similarity.
/// Persists to JSON file on disk for reuse across sessions.
/// </summary>
public class InMemoryVectorStore : IVectorStore
{
    private readonly List<VectorEntry> _entries = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public Task AddAsync(VectorEntry entry, CancellationToken ct = default)
    {
        _lock.Wait(ct);
        try { _entries.Add(entry); }
        finally { _lock.Release(); }
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IEnumerable<VectorEntry> entries, CancellationToken ct = default)
    {
        _lock.Wait(ct);
        try { _entries.AddRange(entries); }
        finally { _lock.Release(); }
        return Task.CompletedTask;
    }

    public Task<List<SearchResult>> SearchAsync(float[] queryEmbedding, int topK = 5, CancellationToken ct = default)
    {
        _lock.Wait(ct);
        try
        {
            var results = _entries
                .Select(e => new SearchResult
                {
                    Entry = e,
                    Score = CosineSimilarity(queryEmbedding, e.Embedding)
                })
                .OrderByDescending(r => r.Score)
                .Take(topK)
                .Where(r => r.Score > 0.1)
                .ToList();

            return Task.FromResult(results);
        }
        finally { _lock.Release(); }
    }

    public Task<int> CountAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_entries.Count);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        _lock.Wait(ct);
        try { _entries.Clear(); }
        finally { _lock.Release(); }
        return Task.CompletedTask;
    }

    public async Task SaveAsync(string path, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is not null) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return;

        await _lock.WaitAsync(ct);
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var entries = JsonSerializer.Deserialize<List<VectorEntry>>(json);
            if (entries is not null)
            {
                _entries.Clear();
                _entries.AddRange(entries);
            }
        }
        finally { _lock.Release(); }
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;

        double dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denominator == 0 ? 0 : dotProduct / denominator;
    }
}
