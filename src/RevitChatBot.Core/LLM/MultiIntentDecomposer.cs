using System.Text.Json.Nodes;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// Decomposes compound user queries into individual sub-queries.
/// E.g., "kiểm tra vận tốc gió và va chạm tầng 3" → 2 separate intents.
/// Uses keyword detection first, then /api/generate for complex cases.
/// </summary>
public class MultiIntentDecomposer
{
    private readonly IOllamaService? _ollama;

    private static readonly string[] SplitMarkers =
    [
        " và ", " and ", " cùng ", " đồng thời ", " sau đó ",
        " then ", " also ", " plus ", ", rồi ", ", sau đó "
    ];

    public MultiIntentDecomposer(IOllamaService? ollama = null)
    {
        _ollama = ollama;
    }

    /// <summary>
    /// Check if a query contains multiple intents.
    /// </summary>
    public static bool HasMultipleIntents(string query)
    {
        var lower = query.ToLowerInvariant();
        return SplitMarkers.Any(m => lower.Contains(m));
    }

    /// <summary>
    /// Decompose a compound query into sub-queries.
    /// Returns the original as a single-item list if decomposition fails.
    /// </summary>
    public async Task<List<string>> DecomposeAsync(
        string query, CancellationToken ct = default)
    {
        var fastResult = DecomposeFast(query);
        if (fastResult.Count > 1) return fastResult;

        if (_ollama == null || !HasMultipleIntents(query))
            return [query];

        try
        {
            var result = await _ollama.GenerateAsync(
                $"""
                Split this MEP engineering query into separate, independent sub-queries.
                Each sub-query should be a complete request.
                Keep the same language.
                Query: "{query}"
                """,
                formatJson: """
                {
                    "type": "object",
                    "properties": {
                        "sub_queries": {
                            "type": "array",
                            "items": {"type": "string"}
                        }
                    },
                    "required": ["sub_queries"]
                }
                """,
                temperature: 0.1,
                numCtx: 2048,
                cancellationToken: ct);

            var parsed = JsonNode.Parse(result);
            var subQueries = parsed?["sub_queries"]?.AsArray()
                .Select(n => n?.GetValue<string>() ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (subQueries is { Count: > 1 })
                return subQueries;
        }
        catch
        {
            // Fall back
        }

        return [query];
    }

    private static List<string> DecomposeFast(string query)
    {
        foreach (var marker in SplitMarkers)
        {
            var idx = query.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx > 5 && idx < query.Length - 5)
            {
                var part1 = query[..idx].Trim();
                var part2 = query[(idx + marker.Length)..].Trim();

                if (part1.Length >= 5 && part2.Length >= 5)
                {
                    var intent1 = MepGlossary.DetectIntent(part1);
                    var intent2 = MepGlossary.DetectIntent(part2);
                    if (intent1 != intent2 || MepGlossary.DetectCategory(part1) != MepGlossary.DetectCategory(part2))
                        return [part1, part2];
                }
            }
        }

        return [query];
    }
}
