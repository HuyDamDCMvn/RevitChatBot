using RevitChatBot.Core.LLM;

namespace RevitChatBot.Core.Skills;

/// <summary>
/// Exports the current Revit view as an image and sends it to a vision-capable
/// Ollama model for analysis. Useful for detecting spatial issues, layout review,
/// or describing what's visible in the current view.
/// Requires a vision model (llava, gemma3, etc.) configured.
/// </summary>
[Skill("analyze_view_image",
    "Export the current Revit view as an image and analyze it using a vision AI model. " +
    "Can describe layout, identify potential spatial issues, detect element placement problems, " +
    "and answer questions about what's visible in the view. " +
    "Requires a vision-capable model (e.g., llava, gemma3).")]
[SkillParameter("question", "string",
    "What to analyze in the view image (e.g., 'Are there any routing issues?', 'Describe the MEP layout')",
    isRequired: true)]
[SkillParameter("resolution", "string",
    "Image resolution: 'low' (512px), 'medium' (1024px), 'high' (2048px). Default: medium.",
    isRequired: false, allowedValues: new[] { "low", "medium", "high" })]
public class AnalyzeViewImageSkill : ISkill
{
    private readonly IOllamaService _ollama;
    private readonly string _visionModel;

    public AnalyzeViewImageSkill(IOllamaService ollama, string visionModel = "llava")
    {
        _ollama = ollama;
        _visionModel = visionModel;
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var question = parameters.GetValueOrDefault("question")?.ToString();
        if (string.IsNullOrWhiteSpace(question))
            return SkillResult.Fail("Parameter 'question' is required.");

        var resolution = parameters.GetValueOrDefault("resolution")?.ToString() ?? "medium";
        int pixelSize = resolution switch
        {
            "low" => 512,
            "high" => 2048,
            _ => 1024
        };

        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        try
        {
            var imageResult = await context.RevitApiInvoker(doc =>
            {
                dynamic dynDoc = doc;
                dynamic? view = dynDoc.ActiveView;
                if (view is null) return null;

                var tempDir = Path.Combine(Path.GetTempPath(), "RevitChatBot_Vision");
                Directory.CreateDirectory(tempDir);
                long viewIdValue = view.Id.Value;
                var imagePath = Path.Combine(tempDir, $"view_{viewIdValue}.png");

                var exportType = doc.GetType().Assembly.GetType(
                    "Autodesk.Revit.DB.ImageExportOptions");
                if (exportType is null) return null;

                var options = Activator.CreateInstance(exportType);
                if (options is null) return null;

                exportType.GetProperty("FilePath")?.SetValue(options, imagePath);
                exportType.GetProperty("PixelSize")?.SetValue(options, pixelSize);
                exportType.GetProperty("ExportRange")?.SetValue(options, 2);

                var exportMethod = doc.GetType().GetMethod("ExportImage",
                    new[] { exportType });
                exportMethod?.Invoke(doc, new[] { options });

                var actualPath = imagePath;
                if (!File.Exists(actualPath))
                {
                    var pngPath = Path.ChangeExtension(imagePath, ".png");
                    if (File.Exists(pngPath)) actualPath = pngPath;
                }

                string viewName = view.Name;
                string viewType = view.ViewType.ToString();

                return File.Exists(actualPath)
                    ? new { Path = actualPath, ViewName = viewName, ViewType = viewType }
                    : (object?)null;
            });

            if (imageResult is null)
                return SkillResult.Fail("Failed to export view image. Check that a view is active.");

            dynamic result = imageResult;
            string imagePath = result.Path;
            string viewName = result.ViewName;
            string viewType = result.ViewType;

            var base64 = VisionSupport.FileToBase64(imagePath);
            if (base64 is null)
                return SkillResult.Fail("Failed to read exported image.");

            var prompt = VisionSupport.BuildVisionPrompt(question,
                $"View: {viewName} ({viewType})");

            var currentOpts = _ollama.GetCurrentOptions();
            var savedModel = currentOpts.Model;

            _ollama.UpdateOptions(o => o.Model = _visionModel);
            try
            {
                var analysis = await ((OllamaService)_ollama).GenerateAsync(
                    prompt, images: [base64], cancellationToken: cancellationToken);

                return SkillResult.Ok(
                    $"Vision analysis of view '{viewName}'",
                    new { viewName, viewType, analysis });
            }
            finally
            {
                _ollama.UpdateOptions(o => o.Model = savedModel);
                try { File.Delete(imagePath); } catch { }
            }
        }
        catch (Exception ex)
        {
            return SkillResult.Fail($"View image analysis failed: {ex.Message}");
        }
    }
}
