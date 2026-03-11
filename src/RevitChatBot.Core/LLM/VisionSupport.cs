namespace RevitChatBot.Core.LLM;

/// <summary>
/// Support for sending images to Ollama vision models via /api/generate.
/// Used with models like llava, gemma3, etc. that accept image input.
/// </summary>
public static class VisionSupport
{
    /// <summary>
    /// Convert image file to base64 for Ollama /api/generate images field.
    /// </summary>
    public static string? FileToBase64(string imagePath)
    {
        if (!File.Exists(imagePath)) return null;
        var bytes = File.ReadAllBytes(imagePath);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Build a vision analysis prompt with the view image.
    /// </summary>
    public static string BuildVisionPrompt(string userQuery, string? viewContext = null)
    {
        var prompt = "You are an MEP engineer analyzing a Revit view image.\n";
        if (!string.IsNullOrEmpty(viewContext))
            prompt += $"Current view context: {viewContext}\n";
        prompt += $"\nUser question: {userQuery}\n";
        prompt += "\nDescribe what you see in the view image and answer the question. " +
                  "Focus on MEP elements: ducts, pipes, equipment, fittings, routing, " +
                  "potential clashes, spatial issues, and labeling.";
        return prompt;
    }
}
