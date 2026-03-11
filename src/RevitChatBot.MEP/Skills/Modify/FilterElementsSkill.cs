using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("filter_view_elements",
    "Apply a visual filter to the active view that highlights elements by category, " +
    "system type, or parameter criteria. Creates or updates a ParameterFilterElement " +
    "with the specified color override. Use for visualizing distributions — e.g., " +
    "color all Supply Air ducts blue, Return ducts red, exhaust ducts gray.")]
[SkillParameter("category", "string",
    "Built-in category to filter (e.g., 'Ducts', 'Pipes', 'Air Terminals', 'Mechanical Equipment')",
    isRequired: true)]
[SkillParameter("parameter_name", "string",
    "Parameter name to filter by (e.g., 'System Type', 'System Classification', 'Diameter'). " +
    "If omitted, filters the entire category.",
    isRequired: false)]
[SkillParameter("parameter_value", "string",
    "Value to match (e.g., 'Supply Air', 'Return Air'). " +
    "Required when parameter_name is specified.",
    isRequired: false)]
[SkillParameter("color", "string",
    "Color for the filtered elements: red, green, blue, yellow, orange, purple, cyan or hex #RRGGBB",
    isRequired: true)]
[SkillParameter("filter_name", "string",
    "Display name for the filter in Revit. Default: auto-generated.",
    isRequired: false)]
public class FilterElementsSkill : ISkill
{
    private static readonly Dictionary<string, (byte R, byte G, byte B)> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"]     = (255, 0, 0),
        ["green"]   = (0, 200, 0),
        ["blue"]    = (0, 0, 255),
        ["yellow"]  = (255, 220, 0),
        ["orange"]  = (255, 140, 0),
        ["purple"]  = (160, 0, 200),
        ["cyan"]    = (0, 200, 200),
        ["magenta"] = (220, 0, 180),
        ["gray"]    = (128, 128, 128),
    };

    private static readonly Dictionary<string, BuiltInCategory> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ducts"]                = BuiltInCategory.OST_DuctCurves,
        ["duct"]                 = BuiltInCategory.OST_DuctCurves,
        ["pipes"]                = BuiltInCategory.OST_PipeCurves,
        ["pipe"]                 = BuiltInCategory.OST_PipeCurves,
        ["air terminals"]        = BuiltInCategory.OST_DuctTerminal,
        ["duct fittings"]        = BuiltInCategory.OST_DuctFitting,
        ["pipe fittings"]        = BuiltInCategory.OST_PipeFitting,
        ["mechanical equipment"] = BuiltInCategory.OST_MechanicalEquipment,
        ["electrical equipment"] = BuiltInCategory.OST_ElectricalEquipment,
        ["sprinklers"]           = BuiltInCategory.OST_Sprinklers,
        ["cable trays"]          = BuiltInCategory.OST_CableTray,
        ["conduits"]             = BuiltInCategory.OST_Conduit,
        ["flex ducts"]           = BuiltInCategory.OST_FlexDuctCurves,
        ["flex pipes"]           = BuiltInCategory.OST_FlexPipeCurves,
        ["duct accessories"]     = BuiltInCategory.OST_DuctAccessory,
        ["pipe accessories"]     = BuiltInCategory.OST_PipeAccessory,
        ["plumbing fixtures"]    = BuiltInCategory.OST_PlumbingFixtures,
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var categoryStr = parameters.GetValueOrDefault("category")?.ToString();
        if (string.IsNullOrWhiteSpace(categoryStr))
            return SkillResult.Fail("Parameter 'category' is required.");

        var colorStr = parameters.GetValueOrDefault("color")?.ToString();
        if (string.IsNullOrWhiteSpace(colorStr))
            return SkillResult.Fail("Parameter 'color' is required.");

        if (!TryParseColor(colorStr, out var r, out var g, out var b))
            return SkillResult.Fail($"Unknown color '{colorStr}'.");

        if (!CategoryMap.TryGetValue(categoryStr.Trim(), out var bic))
            return SkillResult.Fail(
                $"Unknown category '{categoryStr}'. Supported: {string.Join(", ", CategoryMap.Keys.Distinct())}");

        var paramName = parameters.GetValueOrDefault("parameter_name")?.ToString();
        var paramValue = parameters.GetValueOrDefault("parameter_value")?.ToString();
        var filterName = parameters.GetValueOrDefault("filter_name")?.ToString();

        if (string.IsNullOrWhiteSpace(filterName))
            filterName = $"ChatBot_{categoryStr}" +
                         (string.IsNullOrWhiteSpace(paramValue) ? "" : $"_{paramValue}");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var view = document.ActiveView;
            if (view is null) return new { created = false, reason = "No active view" };

            var catId = new ElementId(bic);
            var categories = new List<ElementId> { catId };

            using var tx = new Transaction(document, "Apply view filter");
            tx.Start();

            var existingFilter = new FilteredElementCollector(document)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>()
                .FirstOrDefault(f => f.Name == filterName);

            if (existingFilter is not null)
            {
                document.Delete(existingFilter.Id);
            }

            ParameterFilterElement filter;
            if (!string.IsNullOrWhiteSpace(paramName) && !string.IsNullOrWhiteSpace(paramValue))
            {
                var sharedParamId = FindParameterId(document, bic, paramName);
                if (sharedParamId == ElementId.InvalidElementId)
                {
                    tx.RollBack();
                    return new { created = false, reason = $"Parameter '{paramName}' not found on '{categoryStr}' elements" };
                }

                var rule = ParameterFilterRuleFactory.CreateEqualsRule(sharedParamId, paramValue);
                var elemFilter = new ElementParameterFilter(rule);
                filter = ParameterFilterElement.Create(document, filterName, categories, elemFilter);
            }
            else
            {
                filter = ParameterFilterElement.Create(document, filterName, categories);
            }

            view.AddFilter(filter.Id);
            view.SetFilterVisibility(filter.Id, true);

            var color = new Color(r, g, b);
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(color);
            ogs.SetSurfaceForegroundPatternColor(color);
            ogs.SetSurfaceBackgroundPatternColor(color);

            var solidId = GetSolidFillPatternId(document);
            if (solidId != ElementId.InvalidElementId)
            {
                ogs.SetSurfaceForegroundPatternId(solidId);
                ogs.SetSurfaceBackgroundPatternId(solidId);
            }

            view.SetFilterOverrides(filter.Id, ogs);
            tx.Commit();

            return new { created = true, reason = "OK" };
        });

        dynamic res = result!;
        if (!(bool)res.created)
            return SkillResult.Fail((string)res.reason);

        var desc = string.IsNullOrWhiteSpace(paramName)
            ? $"Applied {colorStr} color filter on all '{categoryStr}' elements in the active view."
            : $"Applied {colorStr} filter on '{categoryStr}' where {paramName} = '{paramValue}'.";

        return SkillResult.Ok(desc, new { filterName, category = categoryStr, color = colorStr });
    }

    private static ElementId FindParameterId(Document doc, BuiltInCategory bic, string paramName)
    {
        using var collector = new FilteredElementCollector(doc);
        var sample = collector
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .FirstOrDefault();
        if (sample is null) return ElementId.InvalidElementId;

        var param = sample.LookupParameter(paramName);
        if (param is null) return ElementId.InvalidElementId;

        if (param.IsShared)
            return new ElementId((long)param.GUID.GetHashCode());

        if (param.Definition is InternalDefinition intDef)
            return new ElementId((long)intDef.BuiltInParameter);

        return ElementId.InvalidElementId;
    }

    private static ElementId GetSolidFillPatternId(Document doc)
    {
        using var collector = new FilteredElementCollector(doc);
        var solidFill = collector
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
        return solidFill?.Id ?? ElementId.InvalidElementId;
    }

    private static bool TryParseColor(string input, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (NamedColors.TryGetValue(input.Trim(), out var named))
        {
            (r, g, b) = named;
            return true;
        }

        var hex = input.TrimStart('#');
        if (hex.Length == 6 &&
            byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out r) &&
            byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out g) &&
            byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out b))
            return true;

        return false;
    }
}
