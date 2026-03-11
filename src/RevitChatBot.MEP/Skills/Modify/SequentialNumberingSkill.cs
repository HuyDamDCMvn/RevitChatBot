using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("sequential_numbering",
    "Assign sequential numbers to elements with customizable prefix, suffix, and zero-padding. " +
    "Supports numbering by spatial order (left-to-right, bottom-to-top) or by given order. " +
    "Target parameter can be Mark, Number, Comments, or any custom parameter. " +
    "Run with action='preview' first to review assignments before applying.")]
[SkillParameter("action", "string",
    "'preview' to show proposed numbering, 'apply' to write values.",
    isRequired: true,
    allowedValues: new[] { "preview", "apply" })]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs to number. If omitted, uses category + level filter.",
    isRequired: false)]
[SkillParameter("category", "string",
    "Category filter when element_ids is omitted: 'equipment', 'ducts', 'pipes', " +
    "'air_terminals', 'sprinklers', 'rooms', 'spaces', 'doors', 'windows', 'grids', " +
    "'electrical', 'plumbing', 'cable_trays', 'conduits', 'generic_models'.",
    isRequired: false)]
[SkillParameter("level", "string",
    "Level name filter when using category mode.",
    isRequired: false)]
[SkillParameter("target_parameter", "string",
    "Parameter to write the number into. Default: 'Mark'.",
    isRequired: false)]
[SkillParameter("prefix", "string",
    "Text before the number (e.g. 'AHU-', 'L2_EQ-', 'RM-'). Default: empty.",
    isRequired: false)]
[SkillParameter("suffix", "string",
    "Text after the number (e.g. '-A', '.01'). Default: empty.",
    isRequired: false)]
[SkillParameter("start_number", "integer",
    "First number in sequence. Default: 1.",
    isRequired: false)]
[SkillParameter("step", "integer",
    "Increment step between numbers. Default: 1.",
    isRequired: false)]
[SkillParameter("zero_padding", "integer",
    "Minimum digits with leading zeros (e.g. 3 produces 001, 002). Default: 0 (no padding).",
    isRequired: false)]
[SkillParameter("sort_order", "string",
    "Spatial sort: 'left_to_right', 'top_to_bottom', 'by_level', 'by_name', 'as_given'. " +
    "Default: 'left_to_right'.",
    isRequired: false,
    allowedValues: new[] { "left_to_right", "top_to_bottom", "by_level", "by_name", "as_given" })]
public class SequentialNumberingSkill : ISkill
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
        ["rooms"] = BuiltInCategory.OST_Rooms,
        ["spaces"] = BuiltInCategory.OST_MEPSpaces,
        ["doors"] = BuiltInCategory.OST_Doors,
        ["windows"] = BuiltInCategory.OST_Windows,
        ["grids"] = BuiltInCategory.OST_Grids,
        ["generic_models"] = BuiltInCategory.OST_GenericModel,
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

        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "preview";
        var idsStr = parameters.GetValueOrDefault("element_ids")?.ToString();
        var categoryStr = parameters.GetValueOrDefault("category")?.ToString();
        var level = parameters.GetValueOrDefault("level")?.ToString();
        var targetParam = parameters.GetValueOrDefault("target_parameter")?.ToString() ?? "Mark";
        var prefix = parameters.GetValueOrDefault("prefix")?.ToString() ?? "";
        var suffix = parameters.GetValueOrDefault("suffix")?.ToString() ?? "";
        var startNumber = ParseInt(parameters.GetValueOrDefault("start_number"), 1);
        var step = ParseInt(parameters.GetValueOrDefault("step"), 1);
        var zeroPadding = ParseInt(parameters.GetValueOrDefault("zero_padding"), 0);
        var sortOrder = parameters.GetValueOrDefault("sort_order")?.ToString() ?? "left_to_right";

        if (string.IsNullOrWhiteSpace(idsStr) && string.IsNullOrWhiteSpace(categoryStr))
            return SkillResult.Fail("Provide either 'element_ids' or 'category' to select elements.");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var elements = !string.IsNullOrWhiteSpace(idsStr)
                ? ResolveByIds(document, idsStr)
                : ResolveByCategory(document, categoryStr!, level);

            if (elements.Count == 0)
                return new NumberingResult { Error = "No elements found matching criteria." };

            var sorted = SortElements(document, elements, sortOrder);

            var assignments = new List<NumberAssignment>();
            for (int i = 0; i < sorted.Count; i++)
            {
                var elem = sorted[i];
                var number = startNumber + i * step;
                var formatted = zeroPadding > 0
                    ? number.ToString().PadLeft(zeroPadding, '0')
                    : number.ToString();
                var fullValue = prefix + formatted + suffix;

                var currentValue = elem.GetParamDisplay(targetParam);

                assignments.Add(new NumberAssignment
                {
                    ElementId = elem.Id.Value,
                    ElementName = elem.Name,
                    Category = elem.Category?.Name ?? "",
                    Level = elem.GetLevelName(document),
                    CurrentValue = currentValue,
                    NewValue = fullValue
                });
            }

            if (action == "apply")
            {
                int applied = 0, failed = 0;
                var failedIds = new List<long>();

                using var tx = new Transaction(document, "Sequential numbering");
                tx.Start();

                foreach (var a in assignments)
                {
                    var elem = document.GetElement(new ElementId(a.ElementId));
                    if (elem is null) { failed++; failedIds.Add(a.ElementId); continue; }

                    if (SetParamSmart(elem, targetParam, a.NewValue))
                        applied++;
                    else
                    {
                        failed++;
                        failedIds.Add(a.ElementId);
                    }
                }

                tx.Commit();

                return new NumberingResult
                {
                    Success = true,
                    Action = "apply",
                    AppliedCount = applied,
                    FailedCount = failed,
                    FailedIds = failedIds,
                    TotalElements = assignments.Count,
                    Pattern = $"{prefix}{{N}}{suffix}",
                    Range = $"{assignments.First().NewValue} → {assignments.Last().NewValue}",
                    Details = assignments.Take(50).ToList()
                };
            }

            return new NumberingResult
            {
                Success = true,
                Action = "preview",
                TotalElements = assignments.Count,
                Pattern = $"{prefix}{{N}}{suffix}",
                Range = $"{assignments.First().NewValue} → {assignments.Last().NewValue}",
                Details = assignments.Take(50).ToList()
            };
        });

        var res = result as NumberingResult;
        if (res is null || !res.Success)
            return SkillResult.Fail(res?.Error ?? "Sequential numbering failed.");

        if (action == "apply")
            return SkillResult.Ok(
                $"Numbered {res.AppliedCount} elements ({res.Pattern}, range: {res.Range})." +
                (res.FailedCount > 0 ? $" {res.FailedCount} failed." : ""),
                result);

        return SkillResult.Ok(
            $"Preview: {res.TotalElements} elements will be numbered ({res.Pattern}, range: {res.Range}). " +
            "Run with action='apply' to execute.",
            result);
    }

    private static List<Element> ResolveByIds(Document doc, string idsStr)
    {
        return idsStr
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s, out var id) ? id : -1)
            .Where(id => id > 0)
            .Select(id => doc.GetElement(new ElementId(id)))
            .Where(e => e is not null)
            .ToList()!;
    }

    private static List<Element> ResolveByCategory(Document doc, string categoryStr, string? level)
    {
        if (!CategoryMap.TryGetValue(categoryStr.Trim(), out var bic))
            return [];

        var collector = new FluentCollector(doc)
            .OfCategory(bic)
            .WhereElementIsNotElementType();

        if (!string.IsNullOrWhiteSpace(level))
            collector.OnLevel(level);

        return collector.ToList();
    }

    private static List<Element> SortElements(Document doc, List<Element> elements, string sortOrder)
    {
        return sortOrder switch
        {
            "top_to_bottom" => elements
                .OrderByDescending(e => e.GetMidpoint()?.Y ?? 0)
                .ThenBy(e => e.GetMidpoint()?.X ?? 0)
                .ToList(),

            "by_level" => elements
                .OrderBy(e => GetLevelElevation(doc, e))
                .ThenBy(e => e.GetMidpoint()?.X ?? 0)
                .ThenBy(e => e.GetMidpoint()?.Y ?? 0)
                .ToList(),

            "by_name" => elements
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),

            "as_given" => elements,

            _ => elements // "left_to_right"
                .OrderBy(e => e.GetMidpoint()?.X ?? 0)
                .ThenBy(e => e.GetMidpoint()?.Y ?? 0)
                .ToList(),
        };
    }

    private static double GetLevelElevation(Document doc, Element e)
    {
        if (e.LevelId is { } lid && lid != ElementId.InvalidElementId)
        {
            if (doc.GetElement(lid) is Level lvl)
                return lvl.Elevation;
        }
        return 0;
    }

    /// <summary>
    /// Tries multiple strategies to set a parameter value as string.
    /// Falls back to integer set for Number-type parameters.
    /// </summary>
    private static bool SetParamSmart(Element elem, string paramName, string value)
    {
        var p = elem.LookupParameter(paramName);
        if (p is null || p.IsReadOnly) return false;

        try
        {
            return p.StorageType switch
            {
                StorageType.String => SetAndReturn(p, value),
                StorageType.Integer when int.TryParse(value, out var intVal) => SetAndReturn(p, intVal),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private static bool SetAndReturn(Parameter p, string v) { p.Set(v); return true; }
    private static bool SetAndReturn(Parameter p, int v) { p.Set(v); return true; }

    private static int ParseInt(object? value, int fallback)
    {
        if (value is int i) return i;
        if (value is long l) return (int)l;
        if (value is string s && int.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }

    private class NumberingResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string Action { get; set; } = "preview";
        public int TotalElements { get; set; }
        public int AppliedCount { get; set; }
        public int FailedCount { get; set; }
        public List<long> FailedIds { get; set; } = [];
        public string Pattern { get; set; } = "";
        public string Range { get; set; } = "";
        public List<NumberAssignment> Details { get; set; } = [];
    }

    private class NumberAssignment
    {
        public long ElementId { get; set; }
        public string ElementName { get; set; } = "";
        public string Category { get; set; } = "";
        public string Level { get; set; } = "";
        public string CurrentValue { get; set; } = "";
        public string NewValue { get; set; } = "";
    }
}
