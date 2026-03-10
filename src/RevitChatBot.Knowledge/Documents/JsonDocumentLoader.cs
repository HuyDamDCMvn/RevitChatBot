using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitChatBot.Knowledge.Documents;

/// <summary>
/// Loads JSON files containing structured MEP standards/specs data.
/// Expected format: array of objects with "content" and optional "category"/"metadata".
/// </summary>
public class JsonDocumentLoader : IDocumentLoader
{
    public bool CanHandle(string sourcePath)
    {
        return Path.GetExtension(sourcePath).Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<List<DocumentChunk>> LoadAsync(string sourcePath, CancellationToken ct = default)
    {
        var json = await File.ReadAllTextAsync(sourcePath, ct);
        var fileName = Path.GetFileName(sourcePath);
        var chunks = new List<DocumentChunk>();

        var node = JsonNode.Parse(json);
        if (node is JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                var item = arr[i];
                var content = item?["content"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(content)) continue;

                var chunk = new DocumentChunk
                {
                    Content = content,
                    Source = fileName,
                    Category = item?["category"]?.GetValue<string>() ?? "",
                    ChunkIndex = i,
                    Metadata = new Dictionary<string, string>
                    {
                        ["file_path"] = sourcePath,
                        ["file_name"] = fileName
                    }
                };

                var meta = item?["metadata"]?.AsObject();
                if (meta is not null)
                {
                    foreach (var (key, val) in meta)
                        chunk.Metadata[key] = val?.ToString() ?? "";
                }

                chunks.Add(chunk);
            }
        }

        return chunks;
    }
}
