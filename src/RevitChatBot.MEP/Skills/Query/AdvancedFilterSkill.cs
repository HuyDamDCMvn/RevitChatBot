using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Query;

[Skill("advanced_filter",
    "Multi-criteria element filter with sorting. Combines category, level, system, " +
    "parameter value, and room/space filters in a single query. Supports sorting by " +
    "one or two parameters. Returns matching elements with option to select in Revit.")]
[SkillParameter("category", "string",
    "Category: 'ducts', 'pipes', 'equipment', 'fittings', 'pipe_fittings', 'air_terminals', " +
    "'electrical', 'plumbing', 'cable_trays', 'conduits', 'sprinklers', 'flex_ducts', " +
    "'flex_pipes', 'duct_accessories', 'pipe_accessories'.",
    isRequired: true)]
[SkillParameter("level", "string",
    "Level name to filter (e.g. 'Level 1'). Supports partial match.",
    isRequired: false)]
[SkillParameter("system_name", "string",
    "System name filter (e.g. 'Supply Air', 'Chilled Water'). Partial match.",
    isRequired: false)]
[SkillParameter("parameter_name", "string",
    "Parameter name to filter by (e.g. 'Size', 'Diameter', 'Mark').",
    isRequired: false)]
[SkillParameter("parameter_operator", "string",
    "Comparison: 'equals', 'contains', 'greater_than', 'less_than', 'not_equals'.",
    isRequired: false,
    allowedValues: new[] { "equals", "contains", "greater_than", "less_than", "not_equals" })]
[SkillParameter("parameter_value", "string",
    "Value to compare against. Required when parameter_name is specified.",
    isRequired: false)]
[SkillParameter("room_name", "string",
    "Filter by room/space name containing this text.",
    isRequired: false)]
[SkillParameter("sort_by", "string",
    "Parameter name to sort results by (e.g. 'Diameter', 'Length', 'Flow'). Optional.",
    isRequired: false)]
[SkillParameter("sort_order", "string",
    "Sort direction: 'asc' (smallest first) or 'desc' (largest first). Default: 'desc'.",
    isRequired: false, allowedValues: new[] { "asc", "desc" })]
[SkillParameter("secondary_sort_by", "string",
    "Second parameter to sort by after primary sort (e.g. 'Flow'). Optional.",
    isRequired: false)]
[SkillParameter("secondary_sort_order", "string",
    "Sort direction for secondary: 'asc' or 'desc'. Default: 'asc'.",
    isRequired: false, allowedValues: new[] { "asc", "desc" })]
[SkillParameter("phase", "string",
    "Filter by phase: 'New Construction', 'Existing', or any phase name. Partial match.",
    isRequired: false)]
[SkillParameter("select_in_revit", "string",
    "'true' to select matching elements in Revit's active view. Default 'false'.",
    isRequired: false)]
[SkillParameter("max_results", "integer",
    "Max elements to return details for. Default 50.",
    isRequired: false)]
public class AdvancedFilterSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ducts"] = BuiltInCategory.OST_DuctCurves,
        ["pipes"] = BuiltInCategory.OST_PipeCurves,
        ["equipment"] = BuiltInCategory.OST_MechanicalEquipment,
        ["fittings"] = BuiltInCategory.OST_DuctFitting,
        ["pipe_fittings"] = BuiltInCategory.OST_PipeFitting,
        ["air_terminals"] = BuiltInCategory.OST_DuctTerminal,
        ["electrical"] = BuiltInCategory.OST_ElectricalEquipment,
        ["plumbing"] = BuiltInCategory.OST_PlumbingFixtures,
        ["cable_trays"] = BuiltInCategory.OST_CableTray,
        ["conduits"] = BuiltInCategory.OST_Conduit,
        ["sprinklers"] = BuiltInCategory.OST_Sprinklers,
        ["flex_ducts"] = BuiltInCategory.OST_FlexDuctCurves,
        ["flex_pipes"] = BuiltInCategory.OST_FlexPipeCurves,
        ["duct_accessories"] = BuiltInCategory.OST_DuctAccessory,
        ["pipe_accessories"] = BuiltInCategory.OST_PipeAccessory,
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var categoryStr = parameters.GetValueOrDefault("category")?.ToString() ?? "";
        if (!CategoryMap.TryGetValue(categoryStr.Trim(), out var bic))
            return SkillResult.Fail($"Unknown category '{categoryStr}'. Supported: {string.Join(", ", CategoryMap.Keys)}");

        var level = parameters.GetValueOrDefault("level")?.ToString();
        var systemName = parameters.GetValueOrDefault("system_name")?.ToString();
        var paramName = parameters.GetValueOrDefault("parameter_name")?.ToString();
        var paramOp = parameters.GetValueOrDefault("parameter_operator")?.ToString() ?? "equals";
        var paramValue = parameters.GetValueOrDefault("parameter_value")?.ToString();
        var roomName = parameters.GetValueOrDefault("room_name")?.ToString();
        var sortBy = parameters.GetValueOrDefault("sort_by")?.ToString();
        var sortOrder = parameters.GetValueOrDefault("sort_order")?.ToString()?.ToLower() ?? "desc";
        var secondarySortBy = parameters.GetValueOrDefault("secondary_sort_by")?.ToString();
        var secondarySortOrder = parameters.GetValueOrDefault("secondary_sort_order")?.ToString()?.ToLower() ?? "asc";
        var phaseFilter = parameters.GetValueOrDefault("phase")?.ToString();
        var selectInRevit = parameters.GetValueOrDefault("select_in_revit")?.ToString()?.ToLower() == "true";
        var maxResults = 50;
        if (parameters.TryGetValue("max_results", out var mr) && mr is not null)
            int.TryParse(mr.ToString(), out maxResults);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var totalBeforeFilter = new FluentCollector(document)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .Count();

            var collector = new FluentCollector(document)
                .OfCategory(bic)
                .WhereElementIsNotElementType();

            if (!string.IsNullOrWhiteSpace(level))
                collector.OnLevel(level);

            if (!string.IsNullOrWhiteSpace(systemName))
                collector.InSystem(systemName);

            if (!string.IsNullOrWhiteSpace(paramName) && !string.IsNullOrWhiteSpace(paramValue))
                collector.WhereParameter(paramName, paramOp, paramValue);

            if (!string.IsNullOrWhiteSpace(roomName))
                collector.InRoom(roomName);

            var elements = collector.ToList();

            if (!string.IsNullOrWhiteSpace(phaseFilter))
            {
                var phases = new FilteredElementCollector(document)
                    .OfClass(typeof(Phase))
                    .Cast<Phase>()
                    .ToList();
                var targetPhase = phases.FirstOrDefault(p =>
                    p.Name.Contains(phaseFilter, StringComparison.OrdinalIgnoreCase));
                if (targetPhase is not null)
                {
                    elements = elements.Where(e =>
                    {
                        var createdPhaseParam = e.get_Parameter(BuiltInParameter.PHASE_CREATED);
                        if (createdPhaseParam is not null && createdPhaseParam.HasValue)
                            return createdPhaseParam.AsElementId() == targetPhase.Id;
                        return false;
                    }).ToList();
                }
            }

            IEnumerable<Element> sorted = elements;
            if (!string.IsNullOrWhiteSpace(sortBy))
            {
                sorted = sortOrder == "asc"
                    ? elements.OrderBy(e => GetNumericParamValue(e, sortBy!))
                    : elements.OrderByDescending(e => GetNumericParamValue(e, sortBy!));

                if (!string.IsNullOrWhiteSpace(secondarySortBy))
                {
                    var orderedSorted = (IOrderedEnumerable<Element>)sorted;
                    sorted = secondarySortOrder == "asc"
                        ? orderedSorted.ThenBy(e => GetNumericParamValue(e, secondarySortBy!))
                        : orderedSorted.ThenByDescending(e => GetNumericParamValue(e, secondarySortBy!));
                }
            }

            var sortedList = sorted.ToList();
            var matchedIds = sortedList.Select(e => e.Id).ToList();

            var selectedInView = false;
            if (selectInRevit && matchedIds.Count > 0)
            {
                try
                {
                    var view = document.ActiveView;
                    if (view is not null)
                    {
                        using var tx = new Transaction(document, "Isolate filtered elements");
                        tx.Start();
                        view.IsolateElementsTemporary(matchedIds);
                        tx.Commit();
                        selectedInView = true;
                    }
                }
                catch { }
            }

            var summaries = sortedList.Take(maxResults).Select(e =>
            {
                var summary = new Dictionary<string, object?>
                {
                    ["id"] = e.Id.Value,
                    ["name"] = e.Name,
                    ["type"] = e.GetTypeName(document),
                    ["level"] = e.GetLevelName(document),
                    ["system"] = e.GetSystemName(),
                    ["size"] = e.GetSize(),
                };

                if (!string.IsNullOrWhiteSpace(sortBy))
                    summary[sortBy!] = GetParamDisplayValue(e, sortBy!);
                if (!string.IsNullOrWhiteSpace(secondarySortBy))
                    summary[secondarySortBy!] = GetParamDisplayValue(e, secondarySortBy!);

                return summary;
            }).ToList();

            object? sortStats = null;
            if (!string.IsNullOrWhiteSpace(sortBy))
            {
                var vals = sortedList.Select(e => GetNumericParamValue(e, sortBy!)).Where(v => v != double.MinValue).ToList();
                if (vals.Count > 0)
                    sortStats = new { min = Math.Round(vals.Min(), 2), max = Math.Round(vals.Max(), 2), avg = Math.Round(vals.Average(), 2) };
            }

            return new
            {
                totalInCategory = totalBeforeFilter,
                matchedCount = sortedList.Count,
                returnedCount = summaries.Count,
                selectedInRevit = selectedInView,
                filters = new
                {
                    category = categoryStr,
                    level,
                    systemName,
                    parameter = paramName is not null ? $"{paramName} {paramOp} {paramValue}" : null,
                    roomName,
                    phase = phaseFilter
                },
                sorting = sortBy is not null ? new { primary = $"{sortBy} {sortOrder}", secondary = secondarySortBy is not null ? $"{secondarySortBy} {secondarySortOrder}" : null } : null,
                sortStats,
                elements = summaries
            };
        });

        dynamic res = result!;
        return SkillResult.Ok(
            $"Found {res.matchedCount} elements matching filters (from {res.totalInCategory} total in category).",
            result);
    }

    private static double GetNumericParamValue(Element e, string paramName)
    {
        var param = e.LookupParameter(paramName);
        if (param is null || !param.HasValue) return double.MinValue;
        if (param.StorageType == StorageType.Double) return param.AsDouble();
        if (param.StorageType == StorageType.Integer) return param.AsInteger();
        var str = param.AsValueString() ?? param.AsString() ?? "";
        return double.TryParse(ExtractNumber(str), out var val) ? val : double.MinValue;
    }

    private static string GetParamDisplayValue(Element e, string paramName)
    {
        var param = e.LookupParameter(paramName);
        if (param is null || !param.HasValue) return "N/A";
        return param.AsValueString() ?? param.AsString() ?? param.AsDouble().ToString("F2");
    }

    private static string ExtractNumber(string s)
    {
        var num = "";
        foreach (var c in s)
        {
            if (char.IsDigit(c) || c == '.' || c == '-') num += c;
            else if (num.Length > 0) break;
        }
        return num;
    }
}
