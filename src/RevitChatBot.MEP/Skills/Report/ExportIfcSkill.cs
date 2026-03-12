using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Report;

[Skill("export_ifc",
    "Export the Revit model to IFC format. Supports IFC2x3 and IFC4. " +
    "Note: MEP material properties may be incomplete in IFC export — this is a known Revit limitation.")]
[SkillParameter("version", "string", "IFC version. Default 'IFC4'.",
    isRequired: false, allowedValues: new[] { "IFC2x3", "IFC4" })]
[SkillParameter("view_name", "string",
    "Export only elements visible in this view (partial match). Omit for entire model.",
    isRequired: false)]
[SkillParameter("file_name", "string", "Output file name (without extension).", isRequired: false)]
public class ExportIfcSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context, Dictionary<string, object?> parameters, CancellationToken ct = default)
    {
        if (context.RevitApiInvoker is null) return SkillResult.Fail("Revit API not available.");

        var version = parameters.GetValueOrDefault("version")?.ToString() ?? "IFC4";
        var viewNameFilter = parameters.GetValueOrDefault("view_name")?.ToString();
        var fileName = parameters.GetValueOrDefault("file_name")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var projectDir = Path.GetDirectoryName(document.PathName);
            if (string.IsNullOrWhiteSpace(projectDir))
                projectDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var exportDir = Path.Combine(projectDir, "ChatBot_Exports", "IFC");
            Directory.CreateDirectory(exportDir);

            var outputName = !string.IsNullOrWhiteSpace(fileName) ? fileName
                : $"{Path.GetFileNameWithoutExtension(document.PathName)}_{DateTime.Now:yyyyMMdd_HHmmss}";

            var options = new IFCExportOptions
            {
                FileVersion = version == "IFC2x3" ? IFCVersion.IFC2x3 : IFCVersion.IFC4,
                ExportBaseQuantities = true,
                SpaceBoundaryLevel = 1,
            };

            if (!string.IsNullOrWhiteSpace(viewNameFilter))
            {
                var view = new FilteredElementCollector(document)
                    .OfClass(typeof(View)).Cast<View>()
                    .FirstOrDefault(v => !v.IsTemplate && v.Name.Contains(viewNameFilter, StringComparison.OrdinalIgnoreCase));

                if (view is not null)
                    options.FilterViewId = view.Id;
            }

            bool success;
            try
            {
                using (var tx = new Transaction(document, "IFC Export"))
                {
                    tx.Start();
                    document.Export(exportDir, outputName + ".ifc", options);
                    tx.Commit();
                }
                var filePath = Path.Combine(exportDir, outputName + ".ifc");
                success = File.Exists(filePath);
                return new
                {
                    success,
                    message = success ? $"Exported IFC ({version}) to {filePath}" : "IFC export failed.",
                    filePath = success ? filePath : null,
                    version,
                    viewFilter = viewNameFilter,
                    mepNote = "MEP material properties may be incomplete in IFC export — this is a known Revit limitation."
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    success = false,
                    message = $"IFC export failed: {ex.Message}",
                    filePath = (string?)null,
                    version,
                    viewFilter = viewNameFilter,
                    mepNote = "MEP material properties may be incomplete in IFC export — this is a known Revit limitation."
                };
            }
        });

        var data = result as dynamic;
        if (data?.success != true) return SkillResult.Fail(data?.message?.ToString() ?? "IFC export failed.");
        return SkillResult.Ok(data?.message?.ToString() ?? "Done.", result);
    }
}
