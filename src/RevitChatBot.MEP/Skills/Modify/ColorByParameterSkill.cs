using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("color_by_parameter",
    "Color-code all elements of a category based on their parameter values. " +
    "Groups elements by unique parameter values and assigns distinct colors to each group. " +
    "Highlights elements with missing/empty parameter values in red. " +
    "Returns a legend table mapping colors to values and element counts. " +
    "Use reset_view_overrides to clear colors afterward.")]
[SkillParameter("category", "string",
    "Element category: 'ducts', 'pipes', 'equipment', 'fittings', 'air_terminals', " +
    "'electrical', 'plumbing', 'cable_trays', 'conduits', 'sprinklers', " +
    "'pipe_fittings', 'duct_accessories', 'pipe_accessories', 'flex_ducts', 'flex_pipes'.",
    isRequired: true)]
[SkillParameter("parameter_name", "string",
    "Parameter name to group by (e.g. 'System Type', 'Size', 'Mark', 'System Classification').",
    isRequired: true)]
[SkillParameter("color_mode", "string",
    "'distinct' = unique hue per value (default), 'gradient' = low-to-high numeric gradient, " +
    "'random' = random colors per value.",
    isRequired: false,
    allowedValues: new[] { "distinct", "gradient", "random" })]
[SkillParameter("level", "string",
    "Optional level name filter to limit scope.",
    isRequired: false)]
[SkillParameter("highlight_missing", "boolean",
    "Highlight elements with empty/null parameter in red. Default: true.",
    isRequired: false)]
[SkillParameter("include_surfaces", "boolean",
    "Also override surface fill patterns (solid fill). Default: true.",
    isRequired: false)]
public class ColorByParameterSkill : ISkill
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

    private const string MissingKey = "<MISSING>";

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

        var paramName = parameters.GetValueOrDefault("parameter_name")?.ToString();
        if (string.IsNullOrWhiteSpace(paramName))
            return SkillResult.Fail("Parameter 'parameter_name' is required.");

        var colorMode = parameters.GetValueOrDefault("color_mode")?.ToString() ?? "distinct";
        var level = parameters.GetValueOrDefault("level")?.ToString();
        var highlightMissing = parameters.GetValueOrDefault("highlight_missing")?.ToString() != "false";
        var includeSurfaces = parameters.GetValueOrDefault("include_surfaces")?.ToString() != "false";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var view = document.ActiveView;
            if (view is null)
                return new ColorByParamResult { Error = "No active view." };

            var collector = new FluentCollector(document)
                .OfCategory(bic)
                .WhereElementIsNotElementType();

            if (!string.IsNullOrWhiteSpace(level))
                collector.OnLevel(level);

            var elements = collector.ToList();
            if (elements.Count == 0)
                return new ColorByParamResult { Error = $"No '{categoryStr}' elements found." };

            var groups = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);
            foreach (var elem in elements)
            {
                var val = elem.GetParamDisplay(paramName!);
                var key = string.IsNullOrWhiteSpace(val) ? MissingKey : val;
                if (!groups.ContainsKey(key))
                    groups[key] = [];
                groups[key].Add(elem.Id);
            }

            var sortedKeys = groups.Keys
                .Where(k => k != MissingKey)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (groups.ContainsKey(MissingKey))
                sortedKeys.Add(MissingKey);

            var palette = GeneratePalette(sortedKeys, colorMode, highlightMissing);
            var solidFillId = GetSolidFillPatternId(document);

            using var tx = new Transaction(document, "Color by parameter");
            tx.Start();

            int overridden = 0;
            foreach (var key in sortedKeys)
            {
                var (r, g, b) = palette[key];
                var color = new Color(r, g, b);
                var ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(color);
                ogs.SetCutLineColor(color);

                if (includeSurfaces)
                {
                    ogs.SetSurfaceForegroundPatternColor(color);
                    ogs.SetSurfaceBackgroundPatternColor(color);
                    ogs.SetCutForegroundPatternColor(color);
                    ogs.SetCutBackgroundPatternColor(color);
                    if (solidFillId != ElementId.InvalidElementId)
                    {
                        ogs.SetSurfaceForegroundPatternId(solidFillId);
                        ogs.SetSurfaceBackgroundPatternId(solidFillId);
                    }
                }

                foreach (var eid in groups[key])
                {
                    view.SetElementOverrides(eid, ogs);
                    overridden++;
                }
            }

            tx.Commit();

            var legend = sortedKeys.Select(k =>
            {
                var (r, g, b) = palette[k];
                return new LegendEntry
                {
                    Value = k,
                    Color = $"#{r:X2}{g:X2}{b:X2}",
                    Count = groups[k].Count
                };
            }).ToList();

            return new ColorByParamResult
            {
                Success = true,
                TotalElements = elements.Count,
                GroupCount = sortedKeys.Count,
                OverriddenCount = overridden,
                MissingCount = groups.ContainsKey(MissingKey) ? groups[MissingKey].Count : 0,
                Legend = legend,
                Category = categoryStr,
                ParameterName = paramName!,
                ColorMode = colorMode
            };
        });

        var res = result as ColorByParamResult;
        if (res is null || !res.Success)
            return SkillResult.Fail(res?.Error ?? "Color by parameter failed.");

        var msg = $"Colored {res.OverriddenCount} elements in {res.GroupCount} groups by '{res.ParameterName}'.";
        if (res.MissingCount > 0)
            msg += $" {res.MissingCount} elements have missing/empty values (shown in red).";

        return SkillResult.Ok(msg, result);
    }

    private static Dictionary<string, (byte R, byte G, byte B)> GeneratePalette(
        List<string> keys, string mode, bool highlightMissing)
    {
        var palette = new Dictionary<string, (byte, byte, byte)>(StringComparer.OrdinalIgnoreCase);
        var nonMissingKeys = keys.Where(k => k != MissingKey).ToList();
        int count = nonMissingKeys.Count;

        switch (mode)
        {
            case "gradient":
                for (int i = 0; i < count; i++)
                {
                    double t = count > 1 ? (double)i / (count - 1) : 0.5;
                    palette[nonMissingKeys[i]] = InterpolateGradient(t);
                }
                break;

            case "random":
                var rng = new Random(keys.Count > 0 ? keys[0].GetHashCode() : 42);
                for (int i = 0; i < count; i++)
                {
                    var h = rng.NextDouble() * 360;
                    palette[nonMissingKeys[i]] = HslToRgb(h, 0.75, 0.5);
                }
                break;

            default: // "distinct"
                for (int i = 0; i < count; i++)
                {
                    double hue = count > 0 ? (double)i / count * 360.0 : 0;
                    palette[nonMissingKeys[i]] = HslToRgb(hue, 0.80, 0.50);
                }
                break;
        }

        if (keys.Contains(MissingKey))
            palette[MissingKey] = highlightMissing ? ((byte)255, (byte)0, (byte)0) : ((byte)180, (byte)180, (byte)180);

        return palette;
    }

    private static (byte R, byte G, byte B) InterpolateGradient(double t)
    {
        // Blue → Cyan → Green → Yellow → Red
        byte r, g, b;
        if (t < 0.25)
        {
            var s = t / 0.25;
            r = 0; g = (byte)(s * 255); b = 255;
        }
        else if (t < 0.5)
        {
            var s = (t - 0.25) / 0.25;
            r = 0; g = 255; b = (byte)((1 - s) * 255);
        }
        else if (t < 0.75)
        {
            var s = (t - 0.5) / 0.25;
            r = (byte)(s * 255); g = 255; b = 0;
        }
        else
        {
            var s = (t - 0.75) / 0.25;
            r = 255; g = (byte)((1 - s) * 255); b = 0;
        }
        return (r, g, b);
    }

    private static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        double m = l - c / 2;
        double rp, gp, bp;

        if (h < 60) { rp = c; gp = x; bp = 0; }
        else if (h < 120) { rp = x; gp = c; bp = 0; }
        else if (h < 180) { rp = 0; gp = c; bp = x; }
        else if (h < 240) { rp = 0; gp = x; bp = c; }
        else if (h < 300) { rp = x; gp = 0; bp = c; }
        else { rp = c; gp = 0; bp = x; }

        return (
            (byte)Math.Clamp((rp + m) * 255, 0, 255),
            (byte)Math.Clamp((gp + m) * 255, 0, 255),
            (byte)Math.Clamp((bp + m) * 255, 0, 255)
        );
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

    private class ColorByParamResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int TotalElements { get; set; }
        public int GroupCount { get; set; }
        public int OverriddenCount { get; set; }
        public int MissingCount { get; set; }
        public List<LegendEntry> Legend { get; set; } = [];
        public string Category { get; set; } = "";
        public string ParameterName { get; set; } = "";
        public string ColorMode { get; set; } = "";
    }

    private class LegendEntry
    {
        public string Value { get; set; } = "";
        public string Color { get; set; } = "";
        public int Count { get; set; }
    }
}
