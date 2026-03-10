namespace RevitChatBot.Core.LLM;

/// <summary>
/// Lightweight early intent detection from partial user input.
/// Can be called on each keypress to pre-load context and pre-filter skills
/// before the user finishes typing. Does NOT call LLM — pure keyword matching.
/// </summary>
public static class StreamingIntentDetector
{
    /// <summary>
    /// Analyze partial input (while user is typing) to predict intent and category.
    /// Returns null if insufficient input to make a prediction.
    /// </summary>
    public static PartialAnalysis? AnalyzePartial(string partialInput)
    {
        if (string.IsNullOrWhiteSpace(partialInput) || partialInput.Length < 4)
            return null;

        var intent = MepGlossary.DetectIntent(partialInput);
        var category = MepGlossary.DetectCategory(partialInput);
        var language = MepGlossary.DetectLanguage(partialInput);

        if (intent == "query" && category == null && partialInput.Length < 10)
            return null;

        return new PartialAnalysis
        {
            Intent = intent,
            Category = category,
            Language = language,
            Confidence = CalculateConfidence(partialInput, intent, category)
        };
    }

    private static double CalculateConfidence(string input, string intent, string? category)
    {
        double confidence = 0.3;

        if (input.Length >= 10) confidence += 0.1;
        if (input.Length >= 20) confidence += 0.1;
        if (intent != "query") confidence += 0.2;
        if (category != null) confidence += 0.2;
        if (input.Contains(' ')) confidence += 0.1;

        return Math.Min(1.0, confidence);
    }
}

public class PartialAnalysis
{
    public string Intent { get; set; } = "query";
    public string? Category { get; set; }
    public string Language { get; set; } = "en";
    public double Confidence { get; set; }
}
