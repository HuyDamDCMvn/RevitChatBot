namespace RevitChatBot.Knowledge.Documents;

/// <summary>
/// Loads .txt and .md files, splitting into chunks by paragraph or fixed size.
/// </summary>
public class TextDocumentLoader : IDocumentLoader
{
    private readonly int _maxChunkSize;
    private readonly int _overlapSize;

    public TextDocumentLoader(int maxChunkSize = 512, int overlapSize = 50)
    {
        _maxChunkSize = maxChunkSize;
        _overlapSize = overlapSize;
    }

    public bool CanHandle(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        return ext is ".txt" or ".md";
    }

    public async Task<List<DocumentChunk>> LoadAsync(string sourcePath, CancellationToken ct = default)
    {
        var text = await File.ReadAllTextAsync(sourcePath, ct);
        var fileName = Path.GetFileName(sourcePath);
        var chunks = new List<DocumentChunk>();

        var paragraphs = text.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);
        var buffer = new System.Text.StringBuilder();
        int chunkIdx = 0;

        foreach (var para in paragraphs)
        {
            var trimmed = para.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (buffer.Length + trimmed.Length > _maxChunkSize && buffer.Length > 0)
            {
                chunks.Add(CreateChunk(buffer.ToString(), fileName, sourcePath, chunkIdx++));
                var overlap = buffer.ToString();
                buffer.Clear();
                if (overlap.Length > _overlapSize)
                    buffer.Append(overlap[^_overlapSize..]);
            }

            if (buffer.Length > 0) buffer.Append("\n\n");
            buffer.Append(trimmed);
        }

        if (buffer.Length > 0)
            chunks.Add(CreateChunk(buffer.ToString(), fileName, sourcePath, chunkIdx));

        return chunks;
    }

    private static DocumentChunk CreateChunk(string content, string fileName, string sourcePath, int index)
    {
        return new DocumentChunk
        {
            Content = content,
            Source = fileName,
            ChunkIndex = index,
            Metadata = new Dictionary<string, string>
            {
                ["file_path"] = sourcePath,
                ["file_name"] = fileName
            }
        };
    }
}
