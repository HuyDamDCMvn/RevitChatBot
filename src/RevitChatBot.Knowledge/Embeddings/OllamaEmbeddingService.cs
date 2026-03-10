using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitChatBot.Knowledge.Embeddings;

/// <summary>
/// Uses Ollama /api/embed endpoint for vector embeddings.
/// Requires an embedding model pulled in Ollama (e.g. nomic-embed-text).
/// </summary>
public class OllamaEmbeddingService : IEmbeddingService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaEmbeddingService(string baseUrl = "http://localhost:11434", string model = "nomic-embed-text")
    {
        _model = model;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMinutes(2)
        };
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var result = await GetEmbeddingsAsync([text], ct);
        return result[0];
    }

    public async Task<List<float[]>> GetEmbeddingsAsync(List<string> texts, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { model = _model, input = texts });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/api/embed", content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var node = JsonNode.Parse(json);

        var embeddings = new List<float[]>();
        var embeddingsArray = node?["embeddings"]?.AsArray();
        if (embeddingsArray is null)
            throw new InvalidOperationException("Ollama /api/embed returned no embeddings.");

        foreach (var embNode in embeddingsArray)
        {
            var vec = embNode?.AsArray()
                .Select(v => v?.GetValue<float>() ?? 0f)
                .ToArray() ?? [];
            embeddings.Add(vec);
        }

        return embeddings;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
