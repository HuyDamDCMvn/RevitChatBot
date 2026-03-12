using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Report;

/// <summary>
/// Exports data from Revit ScheduleView instances to CSV files.
/// Reads actual schedule content as formatted in Revit, preserving calculated fields,
/// sorting, grouping, and filters that the modeler has set up.
/// </summary>
[Skill("export_schedule_data",
    "Export data from Revit schedule views to CSV. Reads the actual schedule table " +
    "as formatted in Revit, preserving calculated values, sorting, and grouping. " +
    "Use action='list' to see available schedules, or specify schedule_name to export.")]
[SkillParameter("action", "string",
    "Action: 'list' to show available schedules, 'export' to export data. Default: 'list'.",
    isRequired: false, allowedValues: new[] { "list", "export" })]
[SkillParameter("schedule_name", "string",
    "Name of the schedule view to export. Use 'all' to export every schedule. " +
    "Supports partial match. Required when action='export'.",
    isRequired: false)]
[SkillParameter("format", "string",
    "Output format: 'csv' (comma-separated) or 'tsv' (tab-separated). Default: 'csv'.",
    isRequired: false, allowedValues: new[] { "csv", "tsv" })]
[SkillParameter("include_headers", "string",
    "Include column headers in output: 'true' or 'false'. Default: 'true'.",
    isRequired: false)]
[SkillParameter("filter_text", "string",
    "Only include rows containing this text (case-insensitive). Optional.",
    isRequired: false)]
public class ExportScheduleDataSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var action = parameters.GetValueOrDefault("action")?.ToString()?.ToLowerInvariant() ?? "list";
        var scheduleName = parameters.GetValueOrDefault("schedule_name")?.ToString();
        var format = parameters.GetValueOrDefault("format")?.ToString()?.ToLowerInvariant() ?? "csv";
        var includeHeaders = parameters.GetValueOrDefault("include_headers")?.ToString() != "false";
        var filterText = parameters.GetValueOrDefault("filter_text")?.ToString();

        if (action == "export" && string.IsNullOrWhiteSpace(scheduleName))
            return SkillResult.Fail("'schedule_name' is required when action='export'.");

        var isExportAll = action == "export"
            && (scheduleName?.Equals("all", StringComparison.OrdinalIgnoreCase) == true
                || scheduleName?.Equals("all_schedules", StringComparison.OrdinalIgnoreCase) == true);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var schedules = new FilteredElementCollector(document)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(vs => !vs.IsTitleblockRevisionSchedule && !vs.IsInternalKeynoteSchedule)
                .OrderBy(vs => vs.Name)
                .ToList();

            if (action == "list")
            {
                var items = schedules.Select(vs =>
                {
                    int rowCount = 0;
                    try
                    {
                        var tableData = vs.GetTableData();
                        var section = tableData.GetSectionData(SectionType.Body);
                        rowCount = section.NumberOfRows;
                    }
                    catch { }

                    return new
                    {
                        name = vs.Name,
                        category = vs.Definition?.CategoryId is { } catId && catId != ElementId.InvalidElementId
                            ? Category.GetCategory(document, catId)?.Name ?? "Multi-category"
                            : "Multi-category",
                        rowCount,
                        id = vs.Id.Value
                    };
                }).ToList();

                return (object)new { action = "list", scheduleCount = items.Count, schedules = items };
            }

            if (isExportAll)
            {
                var folder = System.IO.Path.GetDirectoryName(document.PathName);
                if (string.IsNullOrEmpty(folder))
                    folder = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);

                var exported = new List<object>();
                foreach (var vs in schedules)
                {
                    try
                    {
                        var path = ExportSingleSchedule(vs, format, includeHeaders, filterText, folder);
                        exported.Add(new { name = vs.Name, filePath = path });
                    }
                    catch { }
                }

                return (object)new
                {
                    action = "export_all",
                    exportedCount = exported.Count,
                    totalSchedules = schedules.Count,
                    folder,
                    exported
                };
            }

            var targetSchedule = schedules.FirstOrDefault(vs =>
                vs.Name.Equals(scheduleName, StringComparison.OrdinalIgnoreCase))
                ?? schedules.FirstOrDefault(vs =>
                    vs.Name.Contains(scheduleName!, StringComparison.OrdinalIgnoreCase));

            if (targetSchedule is null)
                return (object)new { action = "export", error = $"Schedule '{scheduleName}' not found.", scheduleCount = schedules.Count };

            var targetFolder = System.IO.Path.GetDirectoryName(document.PathName);
            if (string.IsNullOrEmpty(targetFolder))
                targetFolder = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);

            var filePath = ExportSingleSchedule(targetSchedule, format, includeHeaders, filterText, targetFolder);

            var td = targetSchedule.GetTableData();
            var bodySection = td.GetSectionData(SectionType.Body);

            return (object)new
            {
                action = "export",
                scheduleName = targetSchedule.Name,
                rowCount = bodySection.NumberOfRows - (includeHeaders ? 1 : 0),
                columnCount = bodySection.NumberOfColumns,
                format,
                filePath
            };
        });

        return SkillResult.Ok("Schedule export completed.", result);
    }

    private static string ExportSingleSchedule(
        ViewSchedule vs, string format, bool includeHeaders, string? filterText, string folder)
    {
        var separator = format == "tsv" ? "\t" : ",";
        var td = vs.GetTableData();
        var bodySection = td.GetSectionData(SectionType.Body);
        var columnCount = bodySection.NumberOfColumns;
        var lines = new List<string>();

        if (includeHeaders && columnCount > 0)
        {
            var headers = new List<string>();
            for (int col = 0; col < columnCount; col++)
            {
                try { headers.Add(EscapeCsvField(vs.GetCellText(SectionType.Body, 0, col), separator)); }
                catch { headers.Add(""); }
            }
            lines.Add(string.Join(separator, headers));
        }

        var startRow = includeHeaders ? 1 : 0;
        for (int row = startRow; row < bodySection.NumberOfRows; row++)
        {
            var cells = new List<string>();
            for (int col = 0; col < columnCount; col++)
            {
                try { cells.Add(EscapeCsvField(vs.GetCellText(SectionType.Body, row, col), separator)); }
                catch { cells.Add(""); }
            }

            var line = string.Join(separator, cells);
            if (!string.IsNullOrWhiteSpace(filterText) &&
                !line.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                continue;

            lines.Add(line);
        }

        var ext = format == "tsv" ? ".tsv" : ".csv";
        var fileName = SanitizeFileName(vs.Name) + ext;
        var filePath = System.IO.Path.Combine(folder, fileName);
        System.IO.File.WriteAllLines(filePath, lines);
        return filePath;
    }

    private static string EscapeCsvField(string field, string separator)
    {
        if (string.IsNullOrEmpty(field)) return "";
        if (separator == "," && (field.Contains(',') || field.Contains('"') || field.Contains('\n')))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
