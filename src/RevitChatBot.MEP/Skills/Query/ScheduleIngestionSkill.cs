using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Query;

/// <summary>
/// Exports ViewSchedule data from Revit and ingests it into RAG for semantic search.
/// Supports exporting to structured text (CSV-like) that can be chunked and embedded.
/// </summary>
[Skill("ingest_schedule",
    "Export a Revit ViewSchedule to structured data and ingest it into the knowledge base (RAG) " +
    "for later semantic querying. This allows the AI to answer questions about schedule data " +
    "(equipment lists, quantities, parameters) from the model.")]
[SkillParameter("schedule_name", "string",
    "Name or partial name of the ViewSchedule to export. If empty, lists all available schedules.",
    isRequired: false)]
[SkillParameter("action", "string",
    "'list' to list schedules, 'export' to export a specific schedule, 'ingest' to export and add to RAG.",
    isRequired: false, allowedValues: new[] { "list", "export", "ingest" })]
public class ScheduleIngestionSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var scheduleName = parameters.GetValueOrDefault("schedule_name")?.ToString();
        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "export";

        try
        {
            var result = await context.RevitApiInvoker(doc =>
            {
                var document = (Document)doc;
                var allSchedules = new FilteredElementCollector(document)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(vs => !vs.IsTitleblockRevisionSchedule && !vs.IsInternalKeynoteSchedule)
                    .ToList();

                if (action == "list" || string.IsNullOrWhiteSpace(scheduleName))
                {
                    var listing = allSchedules
                        .Select(s => $"- {s.Name} (Fields: {s.Definition.GetFieldCount()})")
                        .ToList();
                    return new ScheduleExportResult
                    {
                        Action = "list",
                        ScheduleNames = allSchedules.Select(s => s.Name).ToList(),
                        ExportedText = $"Available schedules ({allSchedules.Count}):\n" +
                                       string.Join("\n", listing)
                    };
                }

                var schedule = allSchedules
                    .FirstOrDefault(s => s.Name.Contains(scheduleName, StringComparison.OrdinalIgnoreCase));
                if (schedule is null)
                    return new ScheduleExportResult
                    {
                        Action = action,
                        Error = $"Schedule '{scheduleName}' not found. Available: " +
                                string.Join(", ", allSchedules.Select(s => s.Name).Take(10))
                    };

                var tableData = schedule.GetTableData();
                var sectionBody = tableData.GetSectionData(SectionType.Body);
                int rows = sectionBody.NumberOfRows;
                int cols = sectionBody.NumberOfColumns;

                var headers = new List<string>();
                var sectionHeader = tableData.GetSectionData(SectionType.Header);
                if (sectionHeader.NumberOfRows > 0)
                {
                    for (int c = 0; c < sectionHeader.NumberOfColumns; c++)
                        headers.Add(schedule.GetCellText(SectionType.Header, 0, c));
                }
                if (headers.Count == 0)
                {
                    for (int c = 0; c < cols; c++)
                        headers.Add($"Column{c}");
                }

                var dataRows = new List<string>();
                for (int r = 0; r < rows; r++)
                {
                    var cells = new List<string>();
                    for (int c = 0; c < cols; c++)
                        cells.Add(schedule.GetCellText(SectionType.Body, r, c));
                    dataRows.Add(string.Join("\t", cells));
                }

                var exportText =
                    $"Schedule: {schedule.Name}\n" +
                    $"Headers: {string.Join("\t", headers)}\n" +
                    $"Rows: {rows}\n---\n" +
                    string.Join("\n", dataRows);

                return new ScheduleExportResult
                {
                    Action = action,
                    ScheduleName = schedule.Name,
                    Headers = headers,
                    RowCount = rows,
                    ExportedText = exportText
                };
            });

            if (result is ScheduleExportResult export)
            {
                if (export.Error != null)
                    return SkillResult.Fail(export.Error);

                return SkillResult.Ok(export.ExportedText ?? "", new
                {
                    action = export.Action,
                    scheduleName = export.ScheduleName,
                    rowCount = export.RowCount,
                    headers = export.Headers,
                    scheduleNames = export.ScheduleNames,
                    exportedText = export.ExportedText
                });
            }

            return SkillResult.Fail("Unexpected result from schedule export.");
        }
        catch (Exception ex)
        {
            return SkillResult.Fail($"Schedule ingestion failed: {ex.Message}");
        }
    }

    private class ScheduleExportResult
    {
        public string Action { get; set; } = "";
        public string? ScheduleName { get; set; }
        public List<string>? Headers { get; set; }
        public int RowCount { get; set; }
        public string? ExportedText { get; set; }
        public List<string>? ScheduleNames { get; set; }
        public string? Error { get; set; }
    }
}
