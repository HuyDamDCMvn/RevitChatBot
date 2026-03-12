using System.Text.Json;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Nice3point.Revit.Extensions;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.DataExchange;

/// <summary>
/// Maps data from a table (CSV, schedule, or inline JSON) to Revit elements using
/// flexible matching rules. Unlike BatchUpdateFromCsvSkill which requires an ElementId
/// column, this skill can match rows to elements by parameter values (e.g. Size, Mark, Type).
/// </summary>
[Skill("map_data_table",
    "Map data from a table (CSV, Revit schedule, or inline JSON) to Revit elements using " +
    "flexible matching conditions. Match rows to elements by any parameter value (Size, Mark, Type, etc.), " +
    "not just ElementId. Supports preview before applying changes. " +
    "Use source='selected' to limit scope to currently selected elements.")]
[SkillParameter("data_source", "string",
    "Data source type: 'csv:<path>' for a CSV file, 'schedule:<name>' for a Revit ViewSchedule, " +
    "or 'inline' when table_data parameter contains the data.",
    isRequired: true)]
[SkillParameter("table_data", "string",
    "Inline table data as a JSON array of objects, e.g. [{\"Size\":\"300\",\"Mark\":\"D-001\"}]. " +
    "Used when data_source='inline'.",
    isRequired: false)]
[SkillParameter("match_rules", "string",
    "JSON array of matching rules: [{\"table_column\":\"Size\",\"element_parameter\":\"Size\",\"operator\":\"equals\"}]. " +
    "Operators: 'equals', 'contains', 'starts_with', 'ends_with'. All rules must match (AND logic).",
    isRequired: true)]
[SkillParameter("mappings", "string",
    "JSON array of column-to-parameter mappings: " +
    "[{\"from_column\":\"Mark\",\"to_parameter\":\"Mark\",\"transform\":\"copy\"}]. " +
    "Transforms: 'copy' (default), 'upper', 'lower', 'prefix:TEXT', 'suffix:TEXT', 'format:{0}_rev'.",
    isRequired: true)]
[SkillParameter("category", "string",
    "Target element category: 'ducts', 'pipes', 'equipment', 'fittings', 'electrical', 'plumbing', 'all_mep'. " +
    "Not required when source='selected'.",
    isRequired: false,
    allowedValues: new[] { "ducts", "pipes", "equipment", "fittings", "electrical", "plumbing", "all_mep" })]
[SkillParameter("level_filter", "string",
    "Optional level name to restrict element scope (partial match).",
    isRequired: false)]
[SkillParameter("source", "string",
    "'all' (default) to target all elements in category, 'selected' to target currently selected elements in Revit.",
    isRequired: false, allowedValues: new[] { "all", "selected" })]
[SkillParameter("action", "string",
    "'preview' to show what would change without modifying, 'apply' to update the model.",
    isRequired: true, allowedValues: new[] { "preview", "apply" })]
public class DataTableMappingSkill : ISkill
{
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

        var dataSource = parameters.GetValueOrDefault("data_source")?.ToString();
        var tableDataStr = parameters.GetValueOrDefault("table_data")?.ToString();
        var matchRulesStr = parameters.GetValueOrDefault("match_rules")?.ToString();
        var mappingsStr = parameters.GetValueOrDefault("mappings")?.ToString();
        var category = parameters.GetValueOrDefault("category")?.ToString() ?? "all_mep";
        var levelFilter = parameters.GetValueOrDefault("level_filter")?.ToString();
        var source = parameters.GetValueOrDefault("source")?.ToString() ?? "all";
        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "preview";

        if (string.IsNullOrWhiteSpace(dataSource))
            return SkillResult.Fail("data_source is required.");
        if (string.IsNullOrWhiteSpace(matchRulesStr))
            return SkillResult.Fail("match_rules is required.");
        if (string.IsNullOrWhiteSpace(mappingsStr))
            return SkillResult.Fail("mappings is required.");

        List<MatchRule> matchRules;
        List<ColumnMapping> mappings;
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            matchRules = JsonSerializer.Deserialize<List<MatchRule>>(matchRulesStr, opts) ?? [];
            mappings = JsonSerializer.Deserialize<List<ColumnMapping>>(mappingsStr, opts) ?? [];
        }
        catch (JsonException ex)
        {
            return SkillResult.Fail($"Invalid JSON in match_rules or mappings: {ex.Message}");
        }

        if (matchRules.Count == 0)
            return SkillResult.Fail("At least one match_rule is required.");
        if (mappings.Count == 0)
            return SkillResult.Fail("At least one mapping is required.");

        List<long>? selectionIds = null;
        if (source == "selected")
        {
            selectionIds = context.GetCurrentSelectionIds();
            if (selectionIds is null || selectionIds.Count == 0)
                return SkillResult.Fail("No elements currently selected in Revit.");
        }
        else if (!CategoryMap.ContainsKey(category))
        {
            return SkillResult.Fail($"Unknown category '{category}'. Supported: {string.Join(", ", CategoryMap.Keys)}");
        }

        List<Dictionary<string, string>> tableRows;
        try
        {
            tableRows = await LoadTableDataAsync(context, dataSource, tableDataStr);
        }
        catch (Exception ex)
        {
            return SkillResult.Fail($"Failed to load table data: {ex.Message}");
        }

        if (tableRows.Count == 0)
            return SkillResult.Fail("Table data is empty.");

        var neededColumns = matchRules.Select(r => r.TableColumn)
            .Concat(mappings.Select(m => m.FromColumn))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var firstRow = tableRows[0];
        var missingColumns = neededColumns
            .Where(c => !firstRow.ContainsKey(c))
            .ToList();
        if (missingColumns.Count > 0)
            return SkillResult.Fail($"Table is missing columns: {string.Join(", ", missingColumns)}. " +
                                    $"Available: {string.Join(", ", firstRow.Keys)}");

        CategoryMap.TryGetValue(category, out var bics);
        var capturedSelIds = selectionIds;

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var elements = CollectElements(document, source, capturedSelIds, bics, levelFilter);
            if (elements.Count == 0)
                return new MappingResult { Message = "No target elements found." };

            var changes = new List<ChangeRecord>();
            var matchedRows = 0;
            var unmatchedRows = 0;

            foreach (var row in tableRows)
            {
                var matched = FindMatchingElements(elements, row, matchRules, document);
                if (matched.Count == 0)
                {
                    unmatchedRows++;
                    continue;
                }
                matchedRows++;

                foreach (var elem in matched)
                {
                    foreach (var mapping in mappings)
                    {
                        if (!row.TryGetValue(mapping.FromColumn, out var newValue) ||
                            string.IsNullOrEmpty(newValue))
                            continue;

                        var transformed = ApplyTransform(newValue, mapping.Transform ?? "copy");
                        var param = elem.FindParameter(mapping.ToParameter);
                        if (param is null || param.IsReadOnly) continue;

                        var oldValue = param.AsValueString() ?? param.AsString() ?? "";
                        if (oldValue == transformed) continue;

                        changes.Add(new ChangeRecord
                        {
                            ElementId = elem.Id.Value,
                            ElementName = elem.Name,
                            ParameterName = mapping.ToParameter,
                            OldValue = oldValue,
                            NewValue = transformed,
                            MatchedFromRow = SummarizeRow(row, matchRules)
                        });
                    }
                }
            }

            if (action == "apply" && changes.Count > 0)
            {
                var applied = 0;
                var failed = 0;

                using var tx = new Transaction(document, "Map data table to elements");
                tx.Start();

                foreach (var change in changes)
                {
                    var elem = document.GetElement(new ElementId(change.ElementId));
                    var param = elem?.FindParameter(change.ParameterName);
                    if (param is null) { failed++; continue; }

                    if (SetParameterValue(param, change.NewValue))
                        applied++;
                    else
                        failed++;
                }

                tx.Commit();

                return new MappingResult
                {
                    Success = true,
                    Action = "apply",
                    Message = $"Applied {applied} parameter changes ({failed} failed). " +
                              $"{matchedRows} rows matched, {unmatchedRows} unmatched.",
                    AppliedCount = applied,
                    FailedCount = failed,
                    MatchedRows = matchedRows,
                    UnmatchedRows = unmatchedRows,
                    TotalElements = elements.Count,
                    TotalTableRows = tableRows.Count,
                    Changes = changes.Take(50).ToList()
                };
            }

            return new MappingResult
            {
                Success = true,
                Action = "preview",
                Message = $"Preview: {changes.Count} parameter changes across " +
                          $"{changes.Select(c => c.ElementId).Distinct().Count()} elements. " +
                          $"{matchedRows} rows matched, {unmatchedRows} unmatched.",
                TotalChanges = changes.Count,
                MatchedRows = matchedRows,
                UnmatchedRows = unmatchedRows,
                TotalElements = elements.Count,
                TotalTableRows = tableRows.Count,
                Changes = changes.Take(50).ToList()
            };
        });

        var res = result as MappingResult;
        if (res is null || !res.Success)
            return SkillResult.Fail(res?.Message ?? "Data table mapping failed.");

        return SkillResult.Ok(res.Message, result);
    }

    private static List<Element> CollectElements(
        Document document, string source, List<long>? selectionIds,
        BuiltInCategory[]? bics, string? levelFilter)
    {
        var elements = new List<Element>();

        if (source == "selected" && selectionIds is not null)
        {
            foreach (var id in selectionIds)
            {
                var elem = document.GetElement(new ElementId(id));
                if (elem is not null)
                    elements.Add(elem);
            }
        }
        else if (bics is not null)
        {
            foreach (var bic in bics)
                elements.AddRange(document.GetInstances(bic));
        }

        if (!string.IsNullOrWhiteSpace(levelFilter))
        {
            var levelIds = document.EnumerateInstances<Level>()
                .Where(l => l.Name.Contains(levelFilter, StringComparison.OrdinalIgnoreCase))
                .Select(l => l.Id.Value)
                .ToHashSet();

            elements = elements.Where(e =>
                e.LevelId is { } lid && lid != ElementId.InvalidElementId && levelIds.Contains(lid.Value)
            ).ToList();
        }

        return elements;
    }

    private static List<Element> FindMatchingElements(
        List<Element> elements, Dictionary<string, string> row,
        List<MatchRule> rules, Document document)
    {
        return elements.Where(elem =>
        {
            foreach (var rule in rules)
            {
                if (!row.TryGetValue(rule.TableColumn, out var tableValue) ||
                    string.IsNullOrEmpty(tableValue))
                    return false;

                var elemValue = GetElementParameterValue(elem, rule.ElementParameter, document);
                if (string.IsNullOrEmpty(elemValue))
                    return false;

                if (!MatchesOperator(elemValue, tableValue, rule.Operator ?? "equals"))
                    return false;
            }
            return true;
        }).ToList();
    }

    private static string? GetElementParameterValue(Element elem, string parameterName, Document document)
    {
        if (parameterName.Equals("ElementId", StringComparison.OrdinalIgnoreCase))
            return elem.Id.Value.ToString();
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

    private static bool MatchesOperator(string elemValue, string tableValue, string op)
    {
        return op switch
        {
            "equals" => elemValue.Equals(tableValue, StringComparison.OrdinalIgnoreCase),
            "contains" => elemValue.Contains(tableValue, StringComparison.OrdinalIgnoreCase),
            "starts_with" => elemValue.StartsWith(tableValue, StringComparison.OrdinalIgnoreCase),
            "ends_with" => elemValue.EndsWith(tableValue, StringComparison.OrdinalIgnoreCase),
            _ => elemValue.Equals(tableValue, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string ApplyTransform(string value, string transform)
    {
        if (string.IsNullOrEmpty(transform) || transform == "copy") return value;
        if (transform == "upper") return value.ToUpperInvariant();
        if (transform == "lower") return value.ToLowerInvariant();
        if (transform.StartsWith("prefix:")) return transform[7..] + value;
        if (transform.StartsWith("suffix:")) return value + transform[7..];
        if (transform.Contains("{0}")) return transform.Replace("{0}", value);
        return value;
    }

    private static string SummarizeRow(Dictionary<string, string> row, List<MatchRule> rules)
    {
        return string.Join(", ", rules.Select(r =>
            row.TryGetValue(r.TableColumn, out var v) ? $"{r.TableColumn}={v}" : ""));
    }

    #region Table loading

    private async Task<List<Dictionary<string, string>>> LoadTableDataAsync(
        SkillContext context, string dataSource, string? tableDataStr)
    {
        if (dataSource.Equals("inline", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(tableDataStr))
                throw new ArgumentException("table_data is required when data_source='inline'.");
            return ParseInlineJson(tableDataStr);
        }

        if (dataSource.StartsWith("csv:", StringComparison.OrdinalIgnoreCase))
        {
            var path = dataSource[4..].Trim();
            return LoadCsvFile(path);
        }

        if (dataSource.StartsWith("schedule:", StringComparison.OrdinalIgnoreCase))
        {
            var scheduleName = dataSource[9..].Trim();
            return await LoadScheduleDataAsync(context, scheduleName);
        }

        throw new ArgumentException(
            $"Unknown data_source format: '{dataSource}'. Use 'inline', 'csv:<path>', or 'schedule:<name>'.");
    }

    private static List<Dictionary<string, string>> ParseInlineJson(string json)
    {
        var rows = new List<Dictionary<string, string>>();
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("table_data must be a JSON array of objects.");

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in item.EnumerateObject())
                row[prop.Name] = prop.Value.ToString();
            rows.Add(row);
        }

        return rows;
    }

    private static List<Dictionary<string, string>> LoadCsvFile(string path)
    {
        if (!System.IO.File.Exists(path))
            throw new System.IO.FileNotFoundException($"CSV file not found: {path}");

        var lines = System.IO.File.ReadAllLines(path);
        if (lines.Length < 2)
            throw new ArgumentException("CSV file must have at least a header row and one data row.");

        var headers = ParseCsvLine(lines[0]);
        var rows = new List<Dictionary<string, string>>();

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            var fields = ParseCsvLine(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var j = 0; j < Math.Min(headers.Count, fields.Count); j++)
                row[headers[j]] = fields[j];
            rows.Add(row);
        }

        return rows;
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

    private async Task<List<Dictionary<string, string>>> LoadScheduleDataAsync(
        SkillContext context, string scheduleName)
    {
        if (context.RevitApiInvoker is null)
            throw new InvalidOperationException("Revit API not available for schedule reading.");

        var rows = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var schedules = new FilteredElementCollector(document)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(vs => !vs.IsTitleblockRevisionSchedule && !vs.IsInternalKeynoteSchedule)
                .ToList();

            var schedule = schedules.FirstOrDefault(s =>
                s.Name.Contains(scheduleName, StringComparison.OrdinalIgnoreCase));

            if (schedule is null)
                throw new ArgumentException($"Schedule '{scheduleName}' not found.");

            var tableData = schedule.GetTableData();
            var bodySection = tableData.GetSectionData(SectionType.Body);
            var rowCount = bodySection.NumberOfRows;
            var colCount = bodySection.NumberOfColumns;

            var headers = new List<string>();
            try
            {
                var headerSection = tableData.GetSectionData(SectionType.Header);
                for (var c = 0; c < headerSection.NumberOfColumns; c++)
                    headers.Add(schedule.GetCellText(SectionType.Header, 0, c));
            }
            catch
            {
                for (var c = 0; c < colCount; c++)
                    headers.Add($"Column{c}");
            }

            var result = new List<Dictionary<string, string>>();
            for (var r = 0; r < rowCount; r++)
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var c = 0; c < Math.Min(colCount, headers.Count); c++)
                    row[headers[c]] = schedule.GetCellText(SectionType.Body, r, c);
                result.Add(row);
            }

            return (object)result;
        });

        return rows as List<Dictionary<string, string>> ?? [];
    }

    #endregion

    #region Parameter helpers

    private static bool SetParameterValue(Parameter param, string value)
    {
        try
        {
            return param.StorageType switch
            {
                StorageType.String => Do(() => param.Set(value)),
                StorageType.Double => double.TryParse(value, out var d) && Do(() => param.Set(d)),
                StorageType.Integer => int.TryParse(value, out var i) && Do(() => param.Set(i)),
                _ => false
            };
        }
        catch { return false; }
    }

    private static bool Do(Action action) { action(); return true; }

    #endregion

    #region Models

    private class MatchRule
    {
        public string TableColumn { get; set; } = "";
        public string ElementParameter { get; set; } = "";
        public string? Operator { get; set; } = "equals";
    }

    private class ColumnMapping
    {
        public string FromColumn { get; set; } = "";
        public string ToParameter { get; set; } = "";
        public string? Transform { get; set; } = "copy";
    }

    private class MappingResult
    {
        public bool Success { get; set; }
        public string Action { get; set; } = "preview";
        public string Message { get; set; } = "";
        public int AppliedCount { get; set; }
        public int FailedCount { get; set; }
        public int TotalChanges { get; set; }
        public int MatchedRows { get; set; }
        public int UnmatchedRows { get; set; }
        public int TotalElements { get; set; }
        public int TotalTableRows { get; set; }
        public List<ChangeRecord> Changes { get; set; } = [];
    }

    private class ChangeRecord
    {
        public long ElementId { get; set; }
        public string ElementName { get; set; } = "";
        public string ParameterName { get; set; } = "";
        public string OldValue { get; set; } = "";
        public string NewValue { get; set; } = "";
        public string MatchedFromRow { get; set; } = "";
    }

    #endregion
}
