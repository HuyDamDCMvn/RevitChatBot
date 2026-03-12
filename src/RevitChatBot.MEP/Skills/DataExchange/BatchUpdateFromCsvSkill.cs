using System.IO;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Nice3point.Revit.Extensions;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.DataExchange;

[Skill("batch_update_from_csv",
    "Import parameter values from a CSV file and batch-update Revit elements. " +
    "By default matches rows via an 'ElementId' column. " +
    "Can also match by any parameter value using match_column/match_parameter " +
    "(e.g., match by Mark, Type Name, or Size). " +
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
[SkillParameter("match_column", "string",
    "CSV column name used to identify elements. Default: 'ElementId'. " +
    "Can be any column (e.g. 'Mark', 'Type') when used with match_parameter.",
    isRequired: false)]
[SkillParameter("match_parameter", "string",
    "Revit parameter name to match against match_column values. Default: 'ElementId'. " +
    "Example: if match_column='Mark' and match_parameter='Mark', rows are matched by Mark value.",
    isRequired: false)]
[SkillParameter("category", "string",
    "Element category to scope search. Required when match_parameter is not 'ElementId'. " +
    "Values: 'ducts', 'pipes', 'equipment', 'fittings', 'electrical', 'plumbing', 'all_mep'.",
    isRequired: false,
    allowedValues: new[] { "ducts", "pipes", "equipment", "fittings", "electrical", "plumbing", "all_mep" })]
public class BatchUpdateFromCsvSkill : ISkill
{
    private static readonly HashSet<string> DefaultSkipColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "ElementId", "Name", "Category", "Type", "Level"
    };

    private static readonly Dictionary<string, BuiltInCategory[]> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ducts"] = [BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_FlexDuctCurves],
        ["pipes"] = [BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_FlexPipeCurves],
        ["equipment"] = [BuiltInCategory.OST_MechanicalEquipment],
        ["fittings"] = [BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_PipeFitting],
        ["electrical"] = [BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures],
        ["plumbing"] = [BuiltInCategory.OST_PlumbingFixtures],
        ["all_mep"] = [
            BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_PlumbingFixtures
        ],
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
        var matchColumn = parameters.GetValueOrDefault("match_column")?.ToString() ?? "ElementId";
        var matchParameter = parameters.GetValueOrDefault("match_parameter")?.ToString() ?? "ElementId";
        var categoryStr = parameters.GetValueOrDefault("category")?.ToString();

        var useParameterMatching = !matchColumn.Equals("ElementId", StringComparison.OrdinalIgnoreCase)
                                || !matchParameter.Equals("ElementId", StringComparison.OrdinalIgnoreCase);

        if (useParameterMatching && string.IsNullOrWhiteSpace(categoryStr))
            return SkillResult.Fail("category is required when using parameter-based matching (match_column/match_parameter).");

        BuiltInCategory[]? bics = null;
        if (!string.IsNullOrWhiteSpace(categoryStr))
        {
            if (!CategoryMap.TryGetValue(categoryStr, out bics))
                return SkillResult.Fail($"Unknown category '{categoryStr}'.");
        }

        var allowedParams = string.IsNullOrWhiteSpace(paramFilter)
            ? null
            : paramFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var skipColumns = new HashSet<string>(DefaultSkipColumns, StringComparer.OrdinalIgnoreCase);
        if (useParameterMatching)
            skipColumns.Add(matchColumn);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var resolvedPath = ResolveFilePath(filePath, document);
            if (resolvedPath is null || !File.Exists(resolvedPath))
                return new UpdateResult { Success = false, Message = $"CSV file not found: {filePath}" };

            var (headers, rows) = ParseCsv(resolvedPath);

            var matchColIdx = headers.FindIndex(h => h.Equals(matchColumn, StringComparison.OrdinalIgnoreCase));
            if (matchColIdx < 0)
                return new UpdateResult
                {
                    Success = false,
                    Message = $"CSV must have a '{matchColumn}' column. Available: {string.Join(", ", headers)}"
                };

            var paramColumns = new List<(int Index, string Name)>();
            for (var i = 0; i < headers.Count; i++)
            {
                if (skipColumns.Contains(headers[i])) continue;
                if (allowedParams is not null && !allowedParams.Contains(headers[i])) continue;
                paramColumns.Add((i, headers[i]));
            }

            if (paramColumns.Count == 0)
                return new UpdateResult { Success = false, Message = "No updateable parameter columns found in CSV." };

            Dictionary<string, List<Element>>? paramIndex = null;
            if (useParameterMatching && bics is not null)
            {
                var allElements = new List<Element>();
                foreach (var bic in bics)
                    allElements.AddRange(document.GetInstances(bic));

                paramIndex = new Dictionary<string, List<Element>>(StringComparer.OrdinalIgnoreCase);
                foreach (var elem in allElements)
                {
                    var val = GetParameterValue(elem, matchParameter, document);
                    if (string.IsNullOrEmpty(val)) continue;
                    if (!paramIndex.TryGetValue(val, out var list))
                    {
                        list = [];
                        paramIndex[val] = list;
                    }
                    list.Add(elem);
                }
            }

            var changes = new List<ChangeRecord>();
            var skipped = 0;

            foreach (var row in rows)
            {
                if (matchColIdx >= row.Count) continue;
                var matchValue = row[matchColIdx];
                if (string.IsNullOrWhiteSpace(matchValue)) { skipped++; continue; }

                List<Element> matchedElements;

                if (useParameterMatching && paramIndex is not null)
                {
                    matchedElements = paramIndex.GetValueOrDefault(matchValue) ?? [];
                }
                else
                {
                    if (!long.TryParse(matchValue, out var eid)) { skipped++; continue; }
                    var elem = document.GetElement(new ElementId(eid));
                    matchedElements = elem is not null ? [elem] : [];
                }

                if (matchedElements.Count == 0) { skipped++; continue; }

                foreach (var elem in matchedElements)
                {
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
                            ElementId = elem.Id.Value,
                            ElementName = elem.Name,
                            ParameterName = paramName,
                            OldValue = oldValue,
                            NewValue = newValue
                        });
                    }
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
                    Message = $"Applied {applied} changes ({failed} failed, {skipped} rows skipped). " +
                              $"Matched by: {matchColumn} → {matchParameter}.",
                    AppliedCount = applied,
                    FailedCount = failed,
                    SkippedRows = skipped,
                    Changes = changes.Take(50).ToList()
                };
            }

            return new UpdateResult
            {
                Success = true,
                Message = $"Preview: {changes.Count} parameters would change across " +
                          $"{changes.Select(c => c.ElementId).Distinct().Count()} elements. " +
                          $"Matched by: {matchColumn} → {matchParameter}.",
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

    private static string? GetParameterValue(Element elem, string parameterName, Document document)
    {
        if (parameterName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            return elem.Name;
        if (parameterName.Equals("Category", StringComparison.OrdinalIgnoreCase))
            return elem.Category?.Name;
        if (parameterName.Equals("Type", StringComparison.OrdinalIgnoreCase) ||
            parameterName.Equals("Type Name", StringComparison.OrdinalIgnoreCase))
            return document.GetElement(elem.GetTypeId())?.Name;

        var param = elem.FindParameter(parameterName);
        return param?.AsValueString() ?? param?.AsString();
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
