using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace RevitChatBot.Knowledge.Documents;

/// <summary>
/// Loads PDF files using PdfPig, extracting text page by page
/// and splitting into chunks for RAG indexing.
/// Handles DIN/EN standard PDFs with headers/footers cleanup.
/// </summary>
public partial class PdfDocumentLoader : IDocumentLoader
{
    private readonly int _maxChunkSize;
    private readonly int _overlapSize;

    public PdfDocumentLoader(int maxChunkSize = 800, int overlapSize = 100)
    {
        _maxChunkSize = maxChunkSize;
        _overlapSize = overlapSize;
    }

    public bool CanHandle(string sourcePath)
    {
        return Path.GetExtension(sourcePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public Task<List<DocumentChunk>> LoadAsync(string sourcePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(sourcePath);
        var chunks = new List<DocumentChunk>();

        try
        {
            using var document = PdfDocument.Open(sourcePath);
            var allText = new StringBuilder();

            foreach (var page in document.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                var pageText = page.Text;
                if (string.IsNullOrWhiteSpace(pageText)) continue;

                var cleaned = CleanPageText(pageText);
                if (cleaned.Length > 20)
                {
                    allText.AppendLine(cleaned);
                    allText.AppendLine();
                }
            }

            if (allText.Length == 0)
                return Task.FromResult(chunks);

            var standardInfo = ExtractStandardInfo(fileName);
            chunks = ChunkText(allText.ToString(), fileName, sourcePath, standardInfo);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            chunks.Add(new DocumentChunk
            {
                Content = $"[PDF load error for {fileName}: {ex.Message}]",
                Source = fileName,
                Category = "error",
                ChunkIndex = 0,
                Metadata = new Dictionary<string, string>
                {
                    ["file_path"] = sourcePath,
                    ["file_name"] = fileName,
                    ["error"] = ex.Message
                }
            });
        }

        return Task.FromResult(chunks);
    }

    private List<DocumentChunk> ChunkText(
        string text, string fileName, string sourcePath,
        Dictionary<string, string> standardInfo)
    {
        var chunks = new List<DocumentChunk>();
        var paragraphs = text.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);
        var buffer = new StringBuilder();
        int chunkIdx = 0;

        foreach (var para in paragraphs)
        {
            var trimmed = para.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            if (trimmed.Length < 10) continue;

            if (buffer.Length + trimmed.Length > _maxChunkSize && buffer.Length > 0)
            {
                chunks.Add(CreateChunk(buffer.ToString(), fileName, sourcePath, chunkIdx++, standardInfo));
                var overlap = buffer.ToString();
                buffer.Clear();
                if (overlap.Length > _overlapSize)
                    buffer.Append(overlap[^_overlapSize..]);
            }

            if (buffer.Length > 0) buffer.Append("\n\n");
            buffer.Append(trimmed);
        }

        if (buffer.Length > 30)
            chunks.Add(CreateChunk(buffer.ToString(), fileName, sourcePath, chunkIdx, standardInfo));

        return chunks;
    }

    private static string CleanPageText(string text)
    {
        var cleaned = NormenDownloadPattern().Replace(text, "");
        cleaned = PageMarkerPattern().Replace(cleaned, "");
        cleaned = MultipleNewlinePattern().Replace(cleaned, "\n\n");
        return cleaned.Trim();
    }

    private static Dictionary<string, string> ExtractStandardInfo(string fileName)
    {
        var info = new Dictionary<string, string>();

        var match = StandardNumberPattern().Match(fileName);
        if (match.Success)
        {
            info["standard_number"] = match.Groups[1].Value.Trim();
        }

        if (fileName.Contains("Language English", StringComparison.OrdinalIgnoreCase))
            info["language"] = "English";
        else if (fileName.Contains("Language German English", StringComparison.OrdinalIgnoreCase))
            info["language"] = "German/English";
        else if (fileName.Contains("Language German", StringComparison.OrdinalIgnoreCase))
            info["language"] = "German";

        if (fileName.Contains("Draft", StringComparison.OrdinalIgnoreCase))
            info["status"] = "Draft";

        return info;
    }

    private static DocumentChunk CreateChunk(
        string content, string fileName, string sourcePath,
        int index, Dictionary<string, string> standardInfo)
    {
        var metadata = new Dictionary<string, string>(standardInfo)
        {
            ["file_path"] = sourcePath,
            ["file_name"] = fileName
        };

        var category = DetermineCategory(fileName, content);

        return new DocumentChunk
        {
            Content = content,
            Source = fileName,
            Category = category,
            ChunkIndex = index,
            Metadata = metadata
        };
    }

    private static string DetermineCategory(string fileName, string content)
    {
        var upper = fileName.ToUpperInvariant();

        if (upper.Contains("16282")) return "HVAC - Kitchen Ventilation";
        if (upper.Contains("378") && !upper.Contains("16798")) return "HVAC - Refrigeration";
        if (upper.Contains("5149")) return "HVAC - Refrigeration";
        if (upper.Contains("16798")) return "HVAC - Indoor Environment";
        if (upper.Contains("1946-4")) return "HVAC - Healthcare Ventilation";
        if (upper.Contains("1946-6")) return "HVAC - Residential Ventilation";
        if (upper.Contains("VDI 2078")) return "HVAC - Cooling Load";
        if (upper.Contains("VDI 6022")) return "HVAC - Hygiene";
        if (upper.Contains("1264")) return "HVAC - Radiant Heating/Cooling";
        if (upper.Contains("12828")) return "HVAC - Heating Systems";
        if (upper.Contains("12831")) return "HVAC - Heating Load";
        if (upper.Contains("12171")) return "HVAC - Thermal Insulation";
        if (upper.Contains("4108")) return "Building Physics";
        if (upper.Contains("805") || upper.Contains("806")) return "Plumbing - Water Supply";
        if (upper.Contains("1988")) return "Plumbing - Pipe Sizing";
        if (upper.Contains("12056")) return "Plumbing - Drainage";

        if (content.Contains("ventilation", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("HVAC", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("air conditioning", StringComparison.OrdinalIgnoreCase))
            return "HVAC";

        if (content.Contains("pipe", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("drainage", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("water supply", StringComparison.OrdinalIgnoreCase))
            return "Plumbing";

        return "MEP General";
    }

    [GeneratedRegex(@"Normen-Download.*?\n", RegexOptions.IgnoreCase)]
    private static partial Regex NormenDownloadPattern();

    [GeneratedRegex(@"--\s*\d+\s*of\s*\d+\s*--")]
    private static partial Regex PageMarkerPattern();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleNewlinePattern();

    [GeneratedRegex(@"((?:DIN|VDI|EN|ISO)[\s\w/.-]+?)(?:\s*[–-]\s*Language|\s+\d{5,})")]
    private static partial Regex StandardNumberPattern();
}
