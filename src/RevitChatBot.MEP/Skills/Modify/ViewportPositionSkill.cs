using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("save_restore_viewport",
    "Save or restore viewport positions on sheets for consistent layout across sheets.")]
[SkillParameter("action", "string", "Action: 'save', 'restore', or 'list' saved presets.",
    isRequired: true, allowedValues: new[] { "save", "restore", "list" })]
[SkillParameter("sheet_number", "string", "Sheet number to save from or restore to.", isRequired: false)]
[SkillParameter("preset_name", "string", "Name for the saved preset.", isRequired: false)]
public class ViewportPositionSkill : ISkill
{
    private static readonly string PresetsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RevitChatBot", "viewport_presets");

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context, Dictionary<string, object?> parameters, CancellationToken ct = default)
    {
        if (context.RevitApiInvoker is null) return SkillResult.Fail("Revit API not available.");

        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "list";
        var sheetNumber = parameters.GetValueOrDefault("sheet_number")?.ToString();
        var presetName = parameters.GetValueOrDefault("preset_name")?.ToString() ?? "default";

        if (action == "list")
        {
            Directory.CreateDirectory(PresetsDir);
            var files = Directory.GetFiles(PresetsDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension).ToList();
            return SkillResult.Ok($"Found {files.Count} saved presets.", new { presets = files });
        }

        if (string.IsNullOrWhiteSpace(sheetNumber))
            return SkillResult.Fail("'sheet_number' is required for save/restore.");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var sheet = new FilteredElementCollector(document)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .FirstOrDefault(s => s.SheetNumber.Equals(sheetNumber, StringComparison.OrdinalIgnoreCase));

            if (sheet is null) return new { error = $"Sheet '{sheetNumber}' not found." };

            if (action == "save")
            {
                var viewports = new FilteredElementCollector(document, sheet.Id)
                    .OfClass(typeof(Viewport)).Cast<Viewport>().ToList();

                var presetData = viewports.Select(vp =>
                {
                    var center = vp.GetBoxCenter();
                    var viewElem = document.GetElement(vp.ViewId) as View;
                    return new ViewportPositionData
                    {
                        ViewName = viewElem?.Name ?? "Unknown",
                        CenterX = center.X, CenterY = center.Y, CenterZ = center.Z
                    };
                }).ToList();

                Directory.CreateDirectory(PresetsDir);
                var path = Path.Combine(PresetsDir, $"{presetName}.json");
                var writeOpts = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                File.WriteAllText(path, JsonSerializer.Serialize(presetData, writeOpts));

                return new { error = (string?)null,
                    message = $"Saved {presetData.Count} viewport positions as '{presetName}'.",
                    viewportCount = presetData.Count };
            }

            // restore
            var presetPath = Path.Combine(PresetsDir, $"{presetName}.json");
            if (!File.Exists(presetPath))
                return new { error = $"Preset '{presetName}' not found. Use action='list' to see available presets." };

            var json = File.ReadAllText(presetPath);
            var readOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var positions = JsonSerializer.Deserialize<List<ViewportPositionData>>(json, readOpts) ?? [];

            var sheetViewports = new FilteredElementCollector(document, sheet.Id)
                .OfClass(typeof(Viewport)).Cast<Viewport>().ToList();

            using var tx = new Transaction(document, "Restore Viewport Positions");
            tx.Start();
            int restored = 0;

            foreach (var pos in positions)
            {
                var vp = sheetViewports.FirstOrDefault(v =>
                {
                    var view = document.GetElement(v.ViewId) as View;
                    return view?.Name == pos.ViewName;
                });

                if (vp is not null)
                {
                    vp.SetBoxCenter(new XYZ(pos.CenterX, pos.CenterY, pos.CenterZ));
                    restored++;
                }
            }
            tx.Commit();

            return new { error = (string?)null,
                message = $"Restored {restored}/{positions.Count} viewport positions on sheet {sheetNumber}.",
                restored, total = positions.Count };
        });

        var data = result as dynamic;
        if (data?.error is string err && !string.IsNullOrEmpty(err))
            return SkillResult.Fail(err);
        return SkillResult.Ok(data?.message?.ToString() ?? "Done.", result);
    }

    private class ViewportPositionData
    {
        [JsonPropertyName("viewName")]
        public string ViewName { get; set; } = "";

        [JsonPropertyName("centerX")]
        public double CenterX { get; set; }

        [JsonPropertyName("centerY")]
        public double CenterY { get; set; }

        [JsonPropertyName("centerZ")]
        public double CenterZ { get; set; }
    }
}
