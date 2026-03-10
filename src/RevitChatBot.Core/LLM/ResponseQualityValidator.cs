using RevitChatBot.Core.Models;

namespace RevitChatBot.Core.LLM;

/// <summary>
/// Validates LLM response quality against criteria:
/// - References tool results when available
/// - Correct language match
/// - Uses markdown tables for listings
/// - Not generic/irrelevant
/// Can trigger a retry with guidance for low-quality responses.
/// </summary>
public static class ResponseQualityValidator
{
    /// <summary>
    /// Validate the final response quality. Returns issues found.
    /// </summary>
    public static ValidationResult Validate(
        string response,
        string userQuery,
        List<ChatMessage> messages,
        string expectedLanguage)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(response) || response.Length < 10)
        {
            issues.Add("Response is empty or too short");
            return new ValidationResult { Issues = issues, Score = 0, ShouldRetry = true };
        }

        var toolResults = messages
            .Where(m => m.Role == ChatRole.Tool && !string.IsNullOrEmpty(m.Content))
            .ToList();

        if (toolResults.Count > 0)
        {
            bool referencesData = toolResults.Any(tr =>
            {
                var numbers = System.Text.RegularExpressions.Regex.Matches(tr.Content, @"\d{3,}")
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Value)
                    .Take(5);
                return numbers.Any(n => response.Contains(n));
            });

            if (!referencesData && toolResults.Any(tr => tr.Content.Contains("\"success\":true")))
                issues.Add("Response does not reference specific data from tool results");
        }

        if (expectedLanguage == "vi")
        {
            var viMarkers = new[] { "ống", "thiết", "hệ thống", "kiểm", "phần", "được", "không" };
            int viCount = viMarkers.Count(m => response.Contains(m, StringComparison.OrdinalIgnoreCase));
            if (viCount == 0 && !response.Contains("Element") && response.Length > 100)
                issues.Add("Response may not be in the expected language (Vietnamese)");
        }

        if (toolResults.Count > 0 && response.Contains("\"success\"") == false)
        {
            bool hasListingData = toolResults.Any(tr =>
                tr.Content.Contains("count") || tr.Content.Contains("elements") ||
                tr.Content.Contains("total") || tr.Content.Contains("violations"));

            if (hasListingData && !response.Contains("|") && !response.Contains("```"))
                issues.Add("Response should use markdown table for structured data");
        }

        var genericPhrases = new[]
        {
            "I can help you with", "I'd be happy to", "Let me know if",
            "Sure, I can", "Of course!", "Certainly!",
        };
        if (genericPhrases.Any(p => response.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            && response.Length < 200)
            issues.Add("Response appears generic without specific content");

        double score = 10.0 - issues.Count * 2.5;
        score = Math.Max(0, Math.Min(10, score));

        return new ValidationResult
        {
            Issues = issues,
            Score = score,
            ShouldRetry = score < 4.0 && issues.Count >= 2
        };
    }

    /// <summary>
    /// Build a retry prompt that includes quality feedback.
    /// </summary>
    public static string BuildRetryPrompt(ValidationResult validation)
    {
        var feedback = string.Join("; ", validation.Issues);
        return $"[QUALITY CHECK FAILED — Please improve your response]\n" +
               $"Issues: {feedback}\n" +
               "Re-read the tool results above and provide a more specific, data-driven answer. " +
               "Use markdown tables for listings. Include Element IDs. " +
               "Reply in the same language as the user.";
    }
}

public class ValidationResult
{
    public List<string> Issues { get; set; } = [];
    public double Score { get; set; }
    public bool ShouldRetry { get; set; }
}
