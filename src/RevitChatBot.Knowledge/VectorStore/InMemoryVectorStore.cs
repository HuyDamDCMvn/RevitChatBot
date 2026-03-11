using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RevitChatBot.Knowledge.VectorStore;

/// <summary>
/// In-memory vector store with cosine similarity search.
/// Features: deduplication by content hash, metadata-filtered search,
/// incremental persistence via compact binary format with JSON fallback.
/// </summary>
public class InMemoryVectorStore : IVectorStore
{
    private readonly List<VectorEntry> _entries = [];
    private readonly HashSet<string> _textHashes = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public Task AddAsync(VectorEntry entry, CancellationToken ct = default)
    {
        _lock.Wait(ct);
        try { AddIfNew(entry); }
        finally { _lock.Release(); }
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IEnumerable<VectorEntry> entries, CancellationToken ct = default)
    {
        _lock.Wait(ct);
        try
        {
            foreach (var entry in entries)
                AddIfNew(entry);
        }
        finally { _lock.Release(); }
        return Task.CompletedTask;
    }

    public Task<List<SearchResult>> SearchAsync(float[] queryEmbedding, int topK = 5, CancellationToken ct = default)
    {
        return SearchAsync(queryEmbedding, topK, metadataFilter: null, ct);
    }

    /// <summary>
    /// Search with optional metadata filter (e.g. filter by source, category).
    /// </summary>
    public Task<List<SearchResult>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        Dictionary<string, string>? metadataFilter,
        CancellationToken ct = default)
    {
        _lock.Wait(ct);
        try
        {
            IEnumerable<VectorEntry> candidates = _entries;

            if (metadataFilter is { Count: > 0 })
            {
                candidates = candidates.Where(e =>
                    metadataFilter.All(kv =>
                        e.Metadata.TryGetValue(kv.Key, out var val) &&
                        val.Contains(kv.Value, StringComparison.OrdinalIgnoreCase)));
            }

            var results = candidates
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
        try
        {
            _entries.Clear();
            _textHashes.Clear();
        }
        finally { _lock.Release(); }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Remove all entries from a specific source (for selective re-indexing).
    /// </summary>
    public Task RemoveBySourceAsync(string source, CancellationToken ct = default)
    {
        _lock.Wait(ct);
        try
        {
            var toRemove = _entries
                .Where(e => e.Metadata.TryGetValue("source", out var s) &&
                            s.Equals(source, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var entry in toRemove)
            {
                _entries.Remove(entry);
                _textHashes.Remove(ComputeHash(entry.Text));
            }
        }
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

            var binPath = Path.ChangeExtension(path, ".bin");
            await SaveBinaryAsync(binPath, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task LoadAsync(string path, CancellationToken ct = default)
    {
        var binPath = Path.ChangeExtension(path, ".bin");
        if (File.Exists(binPath))
        {
            await LoadBinaryAsync(binPath, ct);
            return;
        }

        if (!File.Exists(path)) return;

        await _lock.WaitAsync(ct);
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var entries = JsonSerializer.Deserialize<List<VectorEntry>>(json);
            if (entries is not null)
            {
                _entries.Clear();
                _textHashes.Clear();
                foreach (var entry in entries)
                    AddIfNew(entry);
            }
        }
        finally { _lock.Release(); }
    }

    #region Binary persistence (compact: ~10x smaller than JSON for large embedding sets)

    private async Task SaveBinaryAsync(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
        using var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true);

        bw.Write(_entries.Count);
        foreach (var entry in _entries)
        {
            bw.Write(entry.Id);
            bw.Write(entry.Text);
            bw.Write(entry.Embedding.Length);
            foreach (var f in entry.Embedding)
                bw.Write(f);
            bw.Write(entry.Metadata.Count);
            foreach (var kv in entry.Metadata)
            {
                bw.Write(kv.Key);
                bw.Write(kv.Value);
            }
        }
        await fs.FlushAsync(ct);
    }

    private async Task LoadBinaryAsync(string path, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);

            int count = br.ReadInt32();
            _entries.Clear();
            _textHashes.Clear();
            _entries.Capacity = count;

            for (int i = 0; i < count; i++)
            {
                var entry = new VectorEntry
                {
                    Id = br.ReadString(),
                    Text = br.ReadString()
                };
                int embLen = br.ReadInt32();
                entry.Embedding = new float[embLen];
                for (int j = 0; j < embLen; j++)
                    entry.Embedding[j] = br.ReadSingle();
                int metaCount = br.ReadInt32();
                for (int j = 0; j < metaCount; j++)
                    entry.Metadata[br.ReadString()] = br.ReadString();

                AddIfNew(entry);
            }
        }
        finally { _lock.Release(); }
    }

    #endregion

    #region Internal

    private void AddIfNew(VectorEntry entry)
    {
        var hash = ComputeHash(entry.Text);
        if (_textHashes.Add(hash))
            _entries.Add(entry);
    }

    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
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

    #endregion
}
