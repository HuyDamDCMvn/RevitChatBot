using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Report;

[Skill("batch_export_views",
    "Batch export Revit views and sheets to files (PDF, DWG, DWF, or images). " +
    "Supports filtering by name pattern, view type, and discipline. " +
    "Inspired by DiRoots ProSheets. Files are saved to a ChatBot_Exports subfolder.")]
[SkillParameter("format", "string",
    "Export format: 'pdf', 'dwg', 'dwf', 'image'.",
    isRequired: true,
    allowedValues: new[] { "pdf", "dwg", "dwf", "image" })]
[SkillParameter("target", "string",
    "What to export: 'sheets', 'views', 'both'. Default 'sheets'.",
    isRequired: false,
    allowedValues: new[] { "sheets", "views", "both" })]
[SkillParameter("name_filter", "string",
    "Only export views/sheets whose name contains this text (e.g. 'MEP', 'M-', 'Plumbing').",
    isRequired: false)]
[SkillParameter("sheet_number_filter", "string",
    "Only export sheets with number matching this prefix (e.g. 'M-' for MEP sheets).",
    isRequired: false)]
[SkillParameter("image_resolution", "string",
    "For image export: 'low' (72dpi), 'medium' (150dpi), 'high' (300dpi). Default 'medium'.",
    isRequired: false,
    allowedValues: new[] { "low", "medium", "high" })]
public class BatchExportViewsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var format = parameters.GetValueOrDefault("format")?.ToString() ?? "pdf";
        var target = parameters.GetValueOrDefault("target")?.ToString() ?? "sheets";
        var nameFilter = parameters.GetValueOrDefault("name_filter")?.ToString();
        var sheetNumFilter = parameters.GetValueOrDefault("sheet_number_filter")?.ToString();
        var imageRes = parameters.GetValueOrDefault("image_resolution")?.ToString() ?? "medium";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var projectDir = Path.GetDirectoryName(document.PathName);
            if (string.IsNullOrWhiteSpace(projectDir))
                projectDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var exportDir = Path.Combine(projectDir, "ChatBot_Exports", $"{format.ToUpper()}_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(exportDir);

            var viewsToExport = CollectViews(document, target, nameFilter, sheetNumFilter);

            if (viewsToExport.Count == 0)
                return new ExportBatchResult { Message = "No views/sheets match the filter criteria." };

            var exported = new List<string>();
            var failed = new List<string>();

            foreach (var view in viewsToExport)
            {
                try
                {
                    var fileName = SanitizeFileName(view is ViewSheet sheet
                        ? $"{sheet.SheetNumber}_{sheet.Name}"
                        : view.Name);

                    var success = format switch
                    {
                        "pdf" => ExportPdf(document, view, exportDir, fileName),
                        "dwg" => ExportDwg(document, view, exportDir, fileName),
                        "dwf" => ExportDwf(document, view, exportDir, fileName),
                        "image" => ExportImage(document, view, exportDir, fileName, imageRes),
                        _ => false
                    };

                    if (success)
                        exported.Add(fileName);
                    else
                        failed.Add(view.Name);
                }
                catch
                {
                    failed.Add(view.Name);
                }
            }

            return new ExportBatchResult
            {
                Success = true,
                Message = $"Exported {exported.Count}/{viewsToExport.Count} items.",
                OutputDirectory = exportDir,
                ExportedCount = exported.Count,
                FailedCount = failed.Count,
                ExportedFiles = exported,
                FailedViews = failed,
                Format = format.ToUpper()
            };
        });

        var res = result as ExportBatchResult;
        if (res is null || !res.Success)
            return SkillResult.Fail(res?.Message ?? "Export failed.");

        return SkillResult.Ok(
            $"Exported {res.ExportedCount} {res.Format} files to {res.OutputDirectory}" +
            (res.FailedCount > 0 ? $" ({res.FailedCount} failed)" : ""),
            result);
    }

    private static List<View> CollectViews(
        Document doc, string target, string? nameFilter, string? sheetNumFilter)
    {
        var result = new List<View>();

        if (target is "sheets" or "both")
        {
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder);

            if (!string.IsNullOrWhiteSpace(sheetNumFilter))
                sheets = sheets.Where(s => s.SheetNumber.StartsWith(sheetNumFilter, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(nameFilter))
                sheets = sheets.Where(s => s.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)
                                           || s.SheetNumber.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));

            result.AddRange(sheets);
        }

        if (target is "views" or "both")
        {
            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v is not ViewSheet && v is not ViewSchedule
                            && v.ViewType is ViewType.FloorPlan or ViewType.CeilingPlan
                               or ViewType.Section or ViewType.Elevation or ViewType.ThreeD
                               or ViewType.EngineeringPlan or ViewType.AreaPlan);

            if (!string.IsNullOrWhiteSpace(nameFilter))
                views = views.Where(v => v.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));

            result.AddRange(views);
        }

        return result;
    }

    private static bool ExportPdf(Document doc, View view, string dir, string fileName)
    {
        var options = new PDFExportOptions
        {
            FileName = fileName,
            Combine = false,
        };

        var viewIds = new List<ElementId> { view.Id };
        return doc.Export(dir, viewIds, options);
    }

    private static bool ExportDwg(Document doc, View view, string dir, string fileName)
    {
        var options = new DWGExportOptions
        {
            FileVersion = ACADVersion.R2018,
            MergedViews = true,
        };

        var viewIds = new List<ElementId> { view.Id };
        return doc.Export(dir, fileName, viewIds, options);
    }

    private static bool ExportDwf(Document doc, View view, string dir, string fileName)
    {
        var options = new DWFExportOptions { MergedViews = true };
        var viewSet = new ViewSet();
        viewSet.Insert(view);
        return doc.Export(dir, fileName, viewSet, options);
    }

    private static bool ExportImage(Document doc, View view, string dir, string fileName, string resolution)
    {
        var pixelSize = resolution switch
        {
            "low" => 1024,
            "high" => 4096,
            _ => 2048
        };

        var options = new ImageExportOptions
        {
            ExportRange = ExportRange.SetOfViews,
            FilePath = Path.Combine(dir, fileName),
            FitDirection = FitDirectionType.Horizontal,
            HLRandWFViewsFileType = ImageFileType.PNG,
            ImageResolution = resolution switch
            {
                "low" => ImageResolution.DPI_72,
                "high" => ImageResolution.DPI_300,
                _ => ImageResolution.DPI_150
            },
            PixelSize = pixelSize,
            ShadowViewsFileType = ImageFileType.PNG
        };

        options.SetViewsAndSheets(new List<ElementId> { view.Id });
        doc.ExportImage(options);
        return true;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private class ExportBatchResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? OutputDirectory { get; set; }
        public int ExportedCount { get; set; }
        public int FailedCount { get; set; }
        public List<string> ExportedFiles { get; set; } = [];
        public List<string> FailedViews { get; set; } = [];
        public string Format { get; set; } = "";
    }
}
