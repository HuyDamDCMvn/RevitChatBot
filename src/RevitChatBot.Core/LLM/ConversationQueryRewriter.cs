using System.Text.Json.Nodes;
using RevitChatBot.Core.Models;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// Rewrites short/contextual user queries using recent conversation history.
/// Resolves pronouns and implicit references (e.g., "còn ống nước?" → "đếm ống nước tầng 2").
/// Uses /api/generate with structured output for fast, cheap rewriting.
/// </summary>
public class ConversationQueryRewriter
{
    private readonly IOllamaService _ollama;
    private const int MaxRecentMessages = 6;
    private const int ShortQueryThreshold = 30;

    public ConversationQueryRewriter(IOllamaService ollama)
    {
        _ollama = ollama;
    }

    /// <summary>
    /// Determine if the query needs rewriting based on conversation context.
    /// </summary>
    public static bool NeedsRewriting(string query, IReadOnlyList<ChatMessage> history)
    {
        if (history.Count < 2) return false;
        if (query.Length > ShortQueryThreshold) return false;

        var lower = query.ToLowerInvariant();

        var contextualMarkers = new[]
        {
            "còn", "con ", "thế còn", "vậy còn", "thêm", "nữa",
            "cái đó", "cai do", "ở trên", "bên trên", "kết quả",
            "cũng", "tương tự", "giống", "làm lại", "lặp lại",
            "what about", "how about", "also", "same", "again",
            "and the", "for the", "that one", "those", "these",
            "it", "them", "this", "repeat", "redo",
        };

        return contextualMarkers.Any(m => lower.Contains(m))
            || (lower.Length < 15 && !lower.Contains("?") && history.Count >= 2);
    }

    /// <summary>
    /// Rewrite a short contextual query using recent conversation history.
    /// Returns the rewritten query, or the original if rewriting fails.
    /// </summary>
    public async Task<string> RewriteAsync(
        string query,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default)
    {
        if (!NeedsRewriting(query, history)) return query;

        try
        {
            var recentContext = BuildRecentContext(history);
            var prompt = $"""
                Given this conversation context:
                {recentContext}
                
                The user now says: "{query}"
                
                Rewrite the user's message as a complete, self-contained query.
                Keep the same language (Vietnamese or English).
                If the message is already complete, return it unchanged.
                Return ONLY the rewritten query, nothing else.
                """;

            var result = await _ollama.GenerateAsync(
                prompt,
                formatJson: """{"type":"object","properties":{"rewritten_query":{"type":"string"}},"required":["rewritten_query"]}""",
                temperature: 0.1,
                numCtx: 2048,
                cancellationToken: ct);

            var parsed = JsonNode.Parse(result);
            var rewritten = parsed?["rewritten_query"]?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(rewritten) && rewritten.Length > query.Length)
                return rewritten;
        }
        catch
        {
            // Fall back to original query
        }

        return query;
    }

    private static string BuildRecentContext(IReadOnlyList<ChatMessage> history)
    {
        var recent = history
            .Where(m => m.Role is ChatRole.User or ChatRole.Assistant)
            .TakeLast(MaxRecentMessages)
            .Select(m =>
            {
                var role = m.Role == ChatRole.User ? "User" : "Assistant";
                var content = m.Content.Length > 200 ? m.Content[..200] + "..." : m.Content;
                return $"{role}: {content}";
            });

        return string.Join("\n", recent);
    }
}
