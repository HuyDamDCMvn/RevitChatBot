using System.IO;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.DataExchange;

[Skill("batch_update_from_csv",
    "Import parameter values from a CSV file and batch-update Revit elements. " +
    "The CSV must have an 'ElementId' column and columns named after the parameters to update. " +
    "Inspired by DiRoots SheetLink's import-from-Excel workflow.")]
[SkillParameter("file_path", "string",
    "Full path to the CSV file. If not provided, looks for the most recent file in ChatBot_Exports.",
    isRequired: false)]
[SkillParameter("action", "string",
    "'preview' to show what would change without modifying, 'apply' to update the model.",
    isRequired: true,
    allowedValues: new[] { "preview", "apply" })]
[SkillParameter("parameters", "string",
    "Comma-separated list of parameter columns to import. If empty, all non-read-only columns are updated.",
    isRequired: false)]
public class BatchUpdateFromCsvSkill : ISkill
{
    private static readonly HashSet<string> SkipColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "ElementId", "Name", "Category", "Type", "Level"
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var filePath = parameters.GetValueOrDefault("file_path")?.ToString();
        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "preview";
        var paramFilter = parameters.GetValueOrDefault("parameters")?.ToString();

        var allowedParams = string.IsNullOrWhiteSpace(paramFilter)
            ? null
            : paramFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var resolvedPath = ResolveFilePath(filePath, document);
            if (resolvedPath is null || !File.Exists(resolvedPath))
                return new UpdateResult { Success = false, Message = $"CSV file not found: {filePath}" };

            var (headers, rows) = ParseCsv(resolvedPath);
            var idColIdx = headers.IndexOf("ElementId");
            if (idColIdx < 0)
                return new UpdateResult { Success = false, Message = "CSV must have an 'ElementId' column." };

            var paramColumns = new List<(int Index, string Name)>();
            for (var i = 0; i < headers.Count; i++)
            {
                if (SkipColumns.Contains(headers[i])) continue;
                if (allowedParams is not null && !allowedParams.Contains(headers[i])) continue;
                paramColumns.Add((i, headers[i]));
            }

            if (paramColumns.Count == 0)
                return new UpdateResult { Success = false, Message = "No updateable parameter columns found in CSV." };

            var changes = new List<ChangeRecord>();
            var skipped = 0;

            foreach (var row in rows)
            {
                if (idColIdx >= row.Count) continue;
                if (!long.TryParse(row[idColIdx], out var eid)) { skipped++; continue; }

                var elem = document.GetElement(new ElementId(eid));
                if (elem is null) { skipped++; continue; }

                foreach (var (colIdx, paramName) in paramColumns)
                {
                    if (colIdx >= row.Count) continue;
                    var newValue = row[colIdx];
                    if (string.IsNullOrEmpty(newValue)) continue;

                    var param = elem.LookupParameter(paramName);
                    if (param is null || param.IsReadOnly) continue;

                    var oldValue = param.AsValueString() ?? param.AsString() ?? "";
                    if (oldValue == newValue) continue;

                    changes.Add(new ChangeRecord
                    {
                        ElementId = eid,
                        ElementName = elem.Name,
                        ParameterName = paramName,
                        OldValue = oldValue,
                        NewValue = newValue
                    });
                }
            }

            if (action == "apply" && changes.Count > 0)
            {
                var applied = 0;
                var failed = 0;

                using var tx = new Transaction(document, "Batch update from CSV");
                tx.Start();

                foreach (var change in changes)
                {
                    var elem = document.GetElement(new ElementId(change.ElementId));
                    var param = elem?.LookupParameter(change.ParameterName);
                    if (param is null) { failed++; continue; }

                    if (SetParameterValue(param, change.NewValue))
                        applied++;
                    else
                        failed++;
                }

                tx.Commit();

                return new UpdateResult
                {
                    Success = true,
                    Message = $"Applied {applied} changes ({failed} failed, {skipped} rows skipped).",
                    AppliedCount = applied,
                    FailedCount = failed,
                    SkippedRows = skipped,
                    Changes = changes.Take(50).ToList()
                };
            }

            return new UpdateResult
            {
                Success = true,
                Message = $"Preview: {changes.Count} parameters would change across {changes.Select(c => c.ElementId).Distinct().Count()} elements.",
                AppliedCount = 0,
                FailedCount = 0,
                SkippedRows = skipped,
                TotalChanges = changes.Count,
                Changes = changes.Take(50).ToList()
            };
        });

        var res = result as UpdateResult;
        if (res?.Success != true)
            return SkillResult.Fail(res?.Message ?? "Update failed.");

        return SkillResult.Ok(res.Message, result);
    }

    private static string? ResolveFilePath(string? path, Document doc)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            return path;

        var projectDir = Path.GetDirectoryName(doc.PathName);
        if (string.IsNullOrWhiteSpace(projectDir))
            projectDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var exportDir = Path.Combine(projectDir, "ChatBot_Exports");
        if (!Directory.Exists(exportDir)) return path;

        if (!string.IsNullOrWhiteSpace(path))
        {
            var candidate = Path.Combine(exportDir, path);
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(exportDir, path + ".csv");
            if (File.Exists(candidate)) return candidate;
        }

        var latest = Directory.GetFiles(exportDir, "*.csv")
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
        return latest;
    }

    private static (List<string> Headers, List<List<string>> Rows) ParseCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        var headers = new List<string>();
        var rows = new List<List<string>>();

        if (lines.Length == 0) return (headers, rows);

        headers = ParseCsvLine(lines[0]);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            rows.Add(ParseCsvLine(line));
        }

        return (headers, rows);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var regex = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)");
        foreach (var field in regex.Split(line))
        {
            var trimmed = field.Trim();
            if (trimmed.StartsWith('"') && trimmed.EndsWith('"'))
                trimmed = trimmed[1..^1].Replace("\"\"", "\"");
            fields.Add(trimmed);
        }
        return fields;
    }

    private static bool SetParameterValue(Parameter param, string value)
    {
        try
        {
            return param.StorageType switch
            {
                StorageType.String => SetString(param, value),
                StorageType.Double => double.TryParse(value, out var d) && SetDouble(param, d),
                StorageType.Integer => int.TryParse(value, out var i) && SetInt(param, i),
                _ => false
            };
        }
        catch { return false; }
    }

    private static bool SetString(Parameter p, string v) { p.Set(v); return true; }
    private static bool SetDouble(Parameter p, double v) { p.Set(v); return true; }
    private static bool SetInt(Parameter p, int v) { p.Set(v); return true; }

    private class UpdateResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int AppliedCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedRows { get; set; }
        public int TotalChanges { get; set; }
        public List<ChangeRecord> Changes { get; set; } = [];
    }

    private class ChangeRecord
    {
        public long ElementId { get; set; }
        public string ElementName { get; set; } = "";
        public string ParameterName { get; set; } = "";
        public string OldValue { get; set; } = "";
        public string NewValue { get; set; } = "";
    }
}
