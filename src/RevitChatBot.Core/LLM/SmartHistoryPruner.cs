using RevitChatBot.Core.Models;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// Intelligent in-session history pruning.
/// When history exceeds threshold, keeps: recent messages (full), tool results,
/// and summarizes older messages. Removes: thinking steps, cancelled actions.
/// </summary>
public static class SmartHistoryPruner
{
    private const int PruneThreshold = 16;
    private const int KeepRecentCount = 6;

    /// <summary>
    /// Prune history if it exceeds the threshold.
    /// Returns a new list (does not modify original).
    /// </summary>
    public static List<ChatMessage> Prune(List<ChatMessage> history, string? conversationSummary = null)
    {
        if (history.Count <= PruneThreshold) return history;

        var result = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(conversationSummary))
        {
            result.Add(ChatMessage.FromSystem(
                $"[Previous conversation summary]: {conversationSummary}"));
        }

        int splitPoint = history.Count - KeepRecentCount;

        for (int i = 0; i < splitPoint; i++)
        {
            var msg = history[i];
            if (msg.Role == ChatRole.Tool)
            {
                var trimmed = msg.Content.Length > 500
                    ? msg.Content[..500] + "...(truncated)"
                    : msg.Content;
                result.Add(ChatMessage.FromTool(msg.ToolName ?? "tool", trimmed));
            }
            else if (msg.Role == ChatRole.User)
            {
                result.Add(msg);
            }
            else if (msg.Role == ChatRole.Assistant)
            {
                if (msg.ToolCalls is { Count: > 0 })
                {
                    result.Add(ChatMessage.FromAssistant(
                        $"[Called: {string.Join(", ", msg.ToolCalls.Select(tc => tc.FunctionName))}]"));
                }
                else if (!string.IsNullOrWhiteSpace(msg.Content) && msg.Content.Length > 10)
                {
                    var summary = msg.Content.Length > 200
                        ? msg.Content[..200] + "..."
                        : msg.Content;
                    result.Add(ChatMessage.FromAssistant(summary));
                }
            }
        }

        for (int i = splitPoint; i < history.Count; i++)
            result.Add(history[i]);

        return result;
    }

    /// <summary>
    /// Remove only non-essential messages (thinking, cancelled) without summarizing.
    /// Lighter operation for moderate history sizes.
    /// </summary>
    public static List<ChatMessage> RemoveNoise(List<ChatMessage> history)
    {
        return history.Where(m =>
        {
            if (m.Role == ChatRole.Assistant && string.IsNullOrWhiteSpace(m.Content)
                && m.ToolCalls is not { Count: > 0 })
                return false;

            if (m.Content.Contains("User cancelled the", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }).ToList();
    }
}
