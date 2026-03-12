using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Query;

/// <summary>
/// Sums numeric parameter values across selected or filtered elements.
/// Related: query_elements, advanced_filter
/// </summary>
[Skill("sum_parameter_values",
    "Sum a numeric parameter (Length, Area, Volume, etc.) across elements of a category or the current selection. " +
    "Returns the total with unit-aware display values.")]
[SkillParameter("parameter_name", "string",
    "Parameter name to sum (e.g. 'Length', 'Area', 'Volume', 'Width', 'Diameter').",
    isRequired: true)]
[SkillParameter("category", "string",
    "Element category to sum across. Ignored if source='selected'.",
    isRequired: false,
    allowedValues: new[] { "ducts", "pipes", "equipment", "fittings", "cable_trays", "conduits" })]
[SkillParameter("source", "string",
    "'selected' to sum only selected elements, 'category' to sum all of a category. Default 'category'.",
    isRequired: false, allowedValues: new[] { "selected", "category" })]
[SkillParameter("scope", "string",
    "Scope: 'active_view' or 'entire_model'.",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class SumParameterSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ducts"] = BuiltInCategory.OST_DuctCurves,
        ["pipes"] = BuiltInCategory.OST_PipeCurves,
        ["equipment"] = BuiltInCategory.OST_MechanicalEquipment,
        ["fittings"] = BuiltInCategory.OST_DuctFitting,
        ["cable_trays"] = BuiltInCategory.OST_CableTray,
        ["conduits"] = BuiltInCategory.OST_Conduit,
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var paramName = parameters.GetValueOrDefault("parameter_name")?.ToString();
        if (string.IsNullOrWhiteSpace(paramName))
            return SkillResult.Fail("Parameter 'parameter_name' is required.");

        var source = parameters.GetValueOrDefault("source")?.ToString() ?? "category";
        var categoryStr = parameters.GetValueOrDefault("category")?.ToString() ?? "ducts";
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            List<Element> elements;

            if (source == "selected")
            {
                var ids = context.GetCurrentSelectionIds();
                if (ids is null || ids.Count == 0)
                    return new { error = "No elements selected.", total = 0.0, displayTotal = "", count = 0, parameterName = paramName };

                elements = ids
                    .Select(id => document.GetElement(new ElementId(id)))
                    .Where(e => e is not null)
                    .ToList()!;
            }
            else
            {
                if (!CategoryMap.TryGetValue(categoryStr, out var bic))
                    return new { error = $"Unknown category '{categoryStr}'.", total = 0.0, displayTotal = "", count = 0, parameterName = paramName };

                elements = new FluentCollector(document)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .ToList();
            }

            double sum = 0;
            int counted = 0;
            string? displayUnit = null;

            foreach (var e in elements)
            {
                var param = e.LookupParameter(paramName);
                if (param is null || !param.HasValue) continue;

                if (param.StorageType == StorageType.Double)
                {
                    sum += param.AsDouble();
                    counted++;
                    displayUnit ??= param.AsValueString()?.Split(' ').LastOrDefault();
                }
                else if (param.StorageType == StorageType.Integer)
                {
                    sum += param.AsInteger();
                    counted++;
                }
            }

            var displayTotal = displayUnit != null
                ? $"{sum:F2} (internal units) — see displayValues for unit-converted results"
                : $"{sum:F2}";

            // Get display value by summing display strings
            var sampleParam = elements.FirstOrDefault()?.LookupParameter(paramName);
            string displaySum = sum.ToString("F2");
            if (sampleParam?.StorageType == StorageType.Double && sampleParam.HasValue)
            {
                var sampleInternal = sampleParam.AsDouble();
                var sampleDisplay = sampleParam.AsValueString() ?? "";
                if (sampleInternal != 0 && !string.IsNullOrEmpty(sampleDisplay))
                {
                    var parts = sampleDisplay.Split(' ');
                    if (parts.Length >= 2 && double.TryParse(parts[0], out var sampleNum) && sampleNum != 0)
                    {
                        var factor = sampleNum / sampleInternal;
                        displaySum = $"{sum * factor:F2} {string.Join(' ', parts.Skip(1))}";
                    }
                }
            }

            return new
            {
                error = (string?)null,
                total = sum,
                displayTotal = displaySum,
                count = counted,
                parameterName = paramName,
                elementCount = elements.Count,
                source,
                category = source == "category" ? categoryStr : "selection"
            };
        });

        var data = result as dynamic;
        if (data?.error is string err && !string.IsNullOrEmpty(err))
            return SkillResult.Fail(err);

        return SkillResult.Ok(
            $"Sum of '{paramName}': {data?.displayTotal} ({data?.count} elements with value out of {data?.elementCount} total)",
            result);
    }
}
