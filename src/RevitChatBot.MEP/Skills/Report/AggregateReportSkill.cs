using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Report;

/// <summary>
/// General-purpose grouped summary report for any MEP element category.
/// Groups elements by parameter value(s), then aggregates (count, sum, min, max, avg).
/// Output is structured for UI tables and downstream chaining.
/// </summary>
[Skill("aggregate_report",
    "Generate a grouped summary report for any element category. " +
    "Groups by one or two parameters, then counts or sums values. " +
    "Use for: fitting count per level, total duct length per system, " +
    "equipment count per room, etc. Returns structured table data.")]
[SkillParameter("category", "string",
    "Element category: 'ducts', 'pipes', 'fittings', 'pipe_fittings', 'equipment', " +
    "'cable_trays', 'conduits', 'sprinklers', 'air_terminals', 'duct_accessories', " +
    "'pipe_accessories', 'flex_ducts', 'flex_pipes'.",
    isRequired: true)]
[SkillParameter("group_by", "string",
    "Parameter name to group by. Common: 'Level', 'System Name', 'System Classification', " +
    "'Size', 'Type', 'Family', 'Mark'. Case-insensitive lookup.",
    isRequired: true)]
[SkillParameter("secondary_group", "string",
    "Optional second grouping parameter for 2-level grouping " +
    "(e.g. group_by='Level', secondary_group='System Name').",
    isRequired: false)]
[SkillParameter("aggregate", "string",
    "Aggregation function: 'count' (default), 'sum', 'min', 'max', 'avg'.",
    isRequired: false, allowedValues: new[] { "count", "sum", "min", "max", "avg" })]
[SkillParameter("value_parameter", "string",
    "Parameter to aggregate when using sum/min/max/avg (e.g. 'Length', 'Area', 'Diameter'). " +
    "Required for sum/min/max/avg. Ignored for 'count'.",
    isRequired: false)]
[SkillParameter("level", "string",
    "Filter by level name (optional, partial match).", isRequired: false)]
[SkillParameter("system_name", "string",
    "Filter by system name (optional, partial match).", isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' or 'entire_model' (default: entire_model).",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class AggregateReportSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ducts"] = BuiltInCategory.OST_DuctCurves,
        ["pipes"] = BuiltInCategory.OST_PipeCurves,
        ["fittings"] = BuiltInCategory.OST_DuctFitting,
        ["pipe_fittings"] = BuiltInCategory.OST_PipeFitting,
        ["equipment"] = BuiltInCategory.OST_MechanicalEquipment,
        ["cable_trays"] = BuiltInCategory.OST_CableTray,
        ["conduits"] = BuiltInCategory.OST_Conduit,
        ["sprinklers"] = BuiltInCategory.OST_Sprinklers,
        ["air_terminals"] = BuiltInCategory.OST_DuctTerminal,
        ["duct_accessories"] = BuiltInCategory.OST_DuctAccessory,
        ["pipe_accessories"] = BuiltInCategory.OST_PipeAccessory,
        ["flex_ducts"] = BuiltInCategory.OST_FlexDuctCurves,
        ["flex_pipes"] = BuiltInCategory.OST_FlexPipeCurves,
        ["electrical"] = BuiltInCategory.OST_ElectricalEquipment,
        ["plumbing"] = BuiltInCategory.OST_PlumbingFixtures,
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

        var groupBy = parameters.GetValueOrDefault("group_by")?.ToString();
        if (string.IsNullOrWhiteSpace(groupBy))
            return SkillResult.Fail("Parameter 'group_by' is required.");

        var secondaryGroup = parameters.GetValueOrDefault("secondary_group")?.ToString();
        var aggregateMode = parameters.GetValueOrDefault("aggregate")?.ToString()?.ToLowerInvariant() ?? "count";
        var valueParam = parameters.GetValueOrDefault("value_parameter")?.ToString();
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var systemFilter = parameters.GetValueOrDefault("system_name")?.ToString();
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        if (aggregateMode is "sum" or "min" or "max" or "avg" && string.IsNullOrWhiteSpace(valueParam))
            return SkillResult.Fail($"'value_parameter' is required when aggregate='{aggregateMode}'.");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var collector = new FluentCollector(document)
                .OfCategory(bic)
                .WhereElementIsNotElementType();

            if (!string.IsNullOrWhiteSpace(levelFilter))
                collector.OnLevel(levelFilter);
            if (!string.IsNullOrWhiteSpace(systemFilter))
                collector.InSystem(systemFilter);

            var elements = collector.ToList();

            var groups = elements.GroupBy(e => BuildGroupKey(e, groupBy!, secondaryGroup));

            var rows = new List<Dictionary<string, object?>>();
            double grandTotal = 0;
            var allValues = new List<double>();

            foreach (var group in groups.OrderBy(g => g.Key))
            {
                var row = new Dictionary<string, object?>();

                var keys = group.Key.Split('\t');
                row[groupBy!] = keys[0];
                if (secondaryGroup is not null && keys.Length > 1)
                    row[secondaryGroup] = keys[1];

                row["count"] = group.Count();

                if (aggregateMode != "count" && valueParam is not null)
                {
                    var vals = group
                        .Select(e => GetNumericParamValue(e, valueParam))
                        .Where(v => v != double.MinValue)
                        .ToList();

                    if (vals.Count > 0)
                    {
                        var aggregated = aggregateMode switch
                        {
                            "sum" => vals.Sum(),
                            "min" => vals.Min(),
                            "max" => vals.Max(),
                            "avg" => vals.Average(),
                            _ => vals.Count
                        };

                        row[aggregateMode] = Math.Round(aggregated, 3);
                        grandTotal += aggregateMode == "sum" ? aggregated : 0;
                        allValues.AddRange(vals);
                    }
                    else
                    {
                        row[aggregateMode] = 0;
                    }
                }

                rows.Add(row);
            }

            object? summary = null;
            if (aggregateMode != "count" && allValues.Count > 0)
            {
                summary = new
                {
                    totalCount = elements.Count,
                    totalSum = Math.Round(allValues.Sum(), 3),
                    min = Math.Round(allValues.Min(), 3),
                    max = Math.Round(allValues.Max(), 3),
                    avg = Math.Round(allValues.Average(), 3)
                };
            }

            return new
            {
                category = categoryStr,
                totalElements = elements.Count,
                groupCount = rows.Count,
                groupBy,
                secondaryGroup,
                aggregateMode,
                valueParameter = valueParam,
                groups = rows,
                summary
            };
        });

        dynamic res = result!;
        return SkillResult.Ok(
            $"Aggregated {res.totalElements} elements into {res.groupCount} groups.",
            result);
    }

    private static string BuildGroupKey(Element e, string groupBy, string? secondaryGroup)
    {
        var primary = ResolveParamValue(e, groupBy);
        if (secondaryGroup is null) return primary;
        return $"{primary}\t{ResolveParamValue(e, secondaryGroup)}";
    }

    private static string ResolveParamValue(Element e, string paramName)
    {
        var lower = paramName.ToLowerInvariant();

        if (lower is "level")
        {
            var lvlId = e.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId()
                        ?? e.LevelId;
            if (lvlId is not null && lvlId != ElementId.InvalidElementId)
                return e.Document.GetElement(lvlId)?.Name ?? "(no level)";
            return e.LookupParameter("Reference Level")?.AsValueString() ?? "(no level)";
        }

        if (lower is "system name" or "system")
        {
            var sys = e.LookupParameter("System Name")
                       ?? e.LookupParameter("System Type")
                       ?? e.LookupParameter("System Classification");
            return sys?.AsString() ?? sys?.AsValueString() ?? "(no system)";
        }

        if (lower is "type" or "family and type")
            return e.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)?.AsValueString() ?? "(unknown type)";

        if (lower is "family")
            return e.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString() ?? "(unknown family)";

        if (lower is "size")
            return e.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "(no size)";

        var param = e.LookupParameter(paramName);
        if (param is null || !param.HasValue) return "(empty)";
        return param.AsValueString() ?? param.AsString() ?? param.AsDouble().ToString("F2");
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
