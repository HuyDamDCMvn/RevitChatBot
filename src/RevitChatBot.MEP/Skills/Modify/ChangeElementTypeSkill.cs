using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Modify;

/// <summary>
/// Change the type/family of elements. Supports changing by element IDs, by filter
/// (category + level), or for current selection. Very common daily operation for modelers.
/// </summary>
[Skill("change_element_type",
    "Change the type (family and type) of elements. Use to swap duct sizes, pipe types, " +
    "equipment families, fitting types, etc. Supports batch change by category and level filter.")]
[SkillParameter("target_type_name", "string",
    "Name of the target type to change TO (e.g. 'Round Duct 400', 'Pipe Types DN65'). " +
    "Partial match supported.",
    isRequired: true)]
[SkillParameter("source", "string",
    "Source of elements: 'element_ids', 'filter', or 'current_type'. Default: 'filter'.",
    isRequired: false, allowedValues: new[] { "element_ids", "filter", "current_type" })]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs (when source='element_ids').",
    isRequired: false)]
[SkillParameter("current_type_name", "string",
    "Current type name to match and replace (when source='current_type'). Partial match.",
    isRequired: false)]
[SkillParameter("category", "string",
    "Category filter: 'ducts', 'pipes', 'fittings', 'equipment', 'cable_trays', 'conduits'.",
    isRequired: false)]
[SkillParameter("level", "string",
    "Level name filter (optional, partial match).",
    isRequired: false)]
public class ChangeElementTypeSkill : ISkill
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
        ["duct_accessories"] = BuiltInCategory.OST_DuctAccessory,
        ["pipe_accessories"] = BuiltInCategory.OST_PipeAccessory,
        ["air_terminals"] = BuiltInCategory.OST_DuctTerminal,
        ["sprinklers"] = BuiltInCategory.OST_Sprinklers,
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var targetTypeName = parameters.GetValueOrDefault("target_type_name")?.ToString();
        if (string.IsNullOrWhiteSpace(targetTypeName))
            return SkillResult.Fail("'target_type_name' is required.");

        var source = parameters.GetValueOrDefault("source")?.ToString() ?? "filter";
        var elementIdsStr = parameters.GetValueOrDefault("element_ids")?.ToString();
        var currentTypeName = parameters.GetValueOrDefault("current_type_name")?.ToString();
        var categoryStr = parameters.GetValueOrDefault("category")?.ToString();
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var targetType = FindType(document, targetTypeName!, categoryStr);
            if (targetType is null)
                return new { error = $"Target type '{targetTypeName}' not found." };

            List<Element> elements;
            switch (source)
            {
                case "element_ids" when !string.IsNullOrWhiteSpace(elementIdsStr):
                    elements = elementIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => long.TryParse(s.Trim(), out var id) ? document.GetElement(new ElementId(id)) : null)
                        .Where(e => e is not null).ToList()!;
                    break;

                case "current_type" when !string.IsNullOrWhiteSpace(currentTypeName):
                    elements = CollectByCurrentType(document, currentTypeName, categoryStr, levelFilter);
                    break;

                default:
                    if (string.IsNullOrWhiteSpace(categoryStr))
                        return new { error = "When source='filter', 'category' is required." };
                    elements = CollectByFilter(document, categoryStr, levelFilter);
                    break;
            }

            if (elements.Count == 0)
                return new { error = "No matching elements found." };

            int changed = 0, failed = 0;
            using var tx = new Transaction(document, "Change element type");
            tx.Start();
            try
            {
                foreach (var elem in elements)
                {
                    try
                    {
                        elem.ChangeTypeId(targetType.Id);
                        changed++;
                    }
                    catch { failed++; }
                }
                tx.Commit();
            }
            catch (Exception ex)
            {
                if (tx.HasStarted()) tx.RollBack();
                return new { error = $"Transaction failed: {ex.Message}" };
            }

            return new
            {
                targetType = targetType.Name,
                totalElements = elements.Count,
                changed,
                failed
            };
        });

        dynamic res = result!;
        if (((IDictionary<string, object>)res).ContainsKey("error"))
            return SkillResult.Fail(res.error?.ToString() ?? "Change type failed.");

        return SkillResult.Ok($"Changed {res.changed} elements to type '{res.targetType}'.", result);
    }

    private static ElementType? FindType(Document doc, string name, string? categoryHint)
    {
        var allTypes = new FilteredElementCollector(doc)
            .WhereElementIsElementType()
            .Cast<ElementType>()
            .ToList();

        var exact = allTypes.FirstOrDefault(t =>
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        if (!string.IsNullOrWhiteSpace(categoryHint) && CategoryMap.TryGetValue(categoryHint, out var bic))
        {
            var catFiltered = allTypes.Where(t => t.Category?.BuiltInCategory == bic).ToList();
            var match = catFiltered.FirstOrDefault(t =>
                t.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return allTypes.FirstOrDefault(t =>
            t.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static List<Element> CollectByCurrentType(Document doc, string typeName, string? category, string? level)
    {
        var elements = CollectByFilter(doc, category, level);
        return elements.Where(e =>
        {
            var tn = e.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)?.AsValueString() ?? "";
            return tn.Contains(typeName, StringComparison.OrdinalIgnoreCase);
        }).ToList();
    }

    private static List<Element> CollectByFilter(Document doc, string? category, string? level)
    {
        if (string.IsNullOrWhiteSpace(category)) return [];

        if (!CategoryMap.TryGetValue(category, out var bic)) return [];

        var collector = new FluentCollector(doc)
            .OfCategory(bic)
            .WhereElementIsNotElementType();

        if (!string.IsNullOrWhiteSpace(level))
            collector.OnLevel(level);

        return collector.ToList();
    }
}
