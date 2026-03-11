using System.Text;
using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.DataExchange;

[Skill("export_to_csv",
    "Export element data from the Revit model to a CSV file. Supports exporting by category " +
    "with configurable parameters. The file is saved to the project folder and can be opened " +
    "in Excel, Google Sheets, etc. Inspired by DiRoots SheetLink.")]
[SkillParameter("category", "string",
    "Category to export: 'ducts', 'pipes', 'equipment', 'fittings', 'electrical', 'plumbing', " +
    "'spaces', 'rooms', or 'all_mep'.",
    isRequired: true,
    allowedValues: new[] { "ducts", "pipes", "equipment", "fittings", "electrical", "plumbing", "spaces", "rooms", "all_mep" })]
[SkillParameter("parameters", "string",
    "Comma-separated parameter names to include (e.g. 'Size,Length,System Name'). " +
    "If empty, exports all available parameters.",
    isRequired: false)]
[SkillParameter("system_filter", "string",
    "Optional system name to filter elements (e.g. 'Supply Air').",
    isRequired: false)]
[SkillParameter("file_name", "string",
    "Custom file name without extension. Default: 'RevitExport_{category}'.",
    isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' to limit to elements visible in the current view, " +
    "'entire_model' to include all (default: entire_model)",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class ExportToCsvSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory[]> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ducts"] = [BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_FlexDuctCurves],
        ["pipes"] = [BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_FlexPipeCurves],
        ["equipment"] = [BuiltInCategory.OST_MechanicalEquipment],
        ["fittings"] = [BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_PipeFitting],
        ["electrical"] = [BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures],
        ["plumbing"] = [BuiltInCategory.OST_PlumbingFixtures],
        ["spaces"] = [BuiltInCategory.OST_MEPSpaces],
        ["rooms"] = [BuiltInCategory.OST_Rooms],
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

        var category = parameters.GetValueOrDefault("category")?.ToString() ?? "ducts";
        var paramList = parameters.GetValueOrDefault("parameters")?.ToString();
        var systemFilter = parameters.GetValueOrDefault("system_filter")?.ToString();
        var fileName = parameters.GetValueOrDefault("file_name")?.ToString();
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        if (!CategoryMap.TryGetValue(category, out var builtInCats))
            return SkillResult.Fail($"Unknown category '{category}'.");

        var requestedParams = string.IsNullOrWhiteSpace(paramList)
            ? null
            : paramList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elementService = new RevitElementService();

            var elements = new List<Element>();
            foreach (var bic in builtInCats)
            {
                elements.AddRange(
                    ViewScopeHelper.CreateCollector(document, scope)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .ToList());
            }

            if (!string.IsNullOrWhiteSpace(systemFilter))
            {
                elements = elements.Where(e =>
                {
                    var sysParam = e.LookupParameter("System Name")
                                   ?? e.LookupParameter("System Type");
                    return sysParam?.AsString()?.Contains(systemFilter, StringComparison.OrdinalIgnoreCase) == true
                           || sysParam?.AsValueString()?.Contains(systemFilter, StringComparison.OrdinalIgnoreCase) == true;
                }).ToList();
            }

            if (elements.Count == 0)
                return new ExportResult { Success = false, Message = "No elements found." };

            var allParamNames = CollectParameterNames(elements, requestedParams);
            var baseColumns = new[] { "ElementId", "Name", "Category", "Type", "Level" };
            var allColumns = baseColumns.Concat(allParamNames).ToList();

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", allColumns.Select(EscapeCsv)));

            foreach (var elem in elements)
            {
                var row = new List<string>
                {
                    elem.Id.Value.ToString(),
                    EscapeCsv(elem.Name),
                    EscapeCsv(elem.Category?.Name ?? ""),
                    EscapeCsv(document.GetElement(elem.GetTypeId())?.Name ?? ""),
                    EscapeCsv(GetLevel(document, elem))
                };

                var paramDict = elementService.GetElementParameters(elem);
                foreach (var pName in allParamNames)
                    row.Add(EscapeCsv(paramDict.GetValueOrDefault(pName, "")));

                sb.AppendLine(string.Join(",", row));
            }

            var csvContent = sb.ToString();
            var outputName = string.IsNullOrWhiteSpace(fileName)
                ? $"RevitExport_{category}"
                : fileName;

            var projectDir = Path.GetDirectoryName(document.PathName);
            if (string.IsNullOrWhiteSpace(projectDir))
                projectDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var exportDir = Path.Combine(projectDir, "ChatBot_Exports");
            Directory.CreateDirectory(exportDir);
            var filePath = Path.Combine(exportDir, $"{outputName}.csv");

            File.WriteAllText(filePath, csvContent, Encoding.UTF8);

            return new ExportResult
            {
                Success = true,
                Message = "Export complete.",
                FilePath = filePath,
                ElementCount = elements.Count,
                ColumnCount = allColumns.Count,
                CsvPreview = string.Join("\n", csvContent.Split('\n').Take(6))
            };
        });

        var res = result as ExportResult;
        if (res?.Success != true)
            return SkillResult.Fail(res?.Message ?? "Export failed.");

        return SkillResult.Ok(
            $"Exported {res.ElementCount} elements ({res.ColumnCount} columns) to {res.FilePath}", result);
    }

    private static List<string> CollectParameterNames(
        List<Element> elements, HashSet<string>? requestedParams)
    {
        if (requestedParams is { Count: > 0 })
            return requestedParams.ToList();

        var paramNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var elem in elements.Take(50))
        {
            foreach (Parameter param in elem.Parameters)
            {
                if (param.HasValue && param.Definition?.Name is { } name)
                    paramNames.Add(name);
            }
        }

        var priority = new[] { "Size", "Length", "Diameter", "Width", "Height",
            "System Name", "System Type", "System Classification",
            "Offset", "Flow", "Velocity", "Insulation Type", "Comments", "Mark" };

        var ordered = priority.Where(paramNames.Contains).ToList();
        ordered.AddRange(paramNames.Except(ordered, StringComparer.OrdinalIgnoreCase).OrderBy(n => n));
        return ordered.Take(30).ToList();
    }

    private static string GetLevel(Document doc, Element elem)
    {
        if (elem.LevelId is { } lid && lid != ElementId.InvalidElementId)
            return doc.GetElement(lid)?.Name ?? "";
        var levelParam = elem.LookupParameter("Reference Level")
                         ?? elem.LookupParameter("Level");
        return levelParam?.AsValueString() ?? "";
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private class ExportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? FilePath { get; set; }
        public int ElementCount { get; set; }
        public int ColumnCount { get; set; }
        public string? CsvPreview { get; set; }
    }
}
