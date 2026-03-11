using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("override_element_color",
    "Override the color of elements in the active view using Revit's OverrideGraphicSettings. " +
    "This produces a true visual change (not just a bounding box overlay) so it works for " +
    "elements of any size including small fittings, terminals, and accessories. " +
    "The override is view-specific and non-destructive; use clear_overrides to reset. " +
    "Colors: red, green, blue, yellow, orange, purple, cyan, magenta, or custom RGB.")]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs to colorize (e.g., '123456,789012')",
    isRequired: true)]
[SkillParameter("color", "string",
    "Color name (red, green, blue, yellow, orange, purple, cyan, magenta) or RGB hex (#FF0000)",
    isRequired: true)]
[SkillParameter("include_surfaces", "boolean",
    "Also override surface foreground/background patterns. Default: true.",
    isRequired: false)]
[SkillParameter("transparency", "integer",
    "Transparency 0-100 (0 = opaque). Default: 0.",
    isRequired: false)]
public class OverrideColorSkill : ISkill
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
        ["white"]   = (255, 255, 255),
        ["black"]   = (0, 0, 0),
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var idsStr = parameters.GetValueOrDefault("element_ids")?.ToString();
        if (string.IsNullOrWhiteSpace(idsStr))
            return SkillResult.Fail("Parameter 'element_ids' is required.");

        var colorStr = parameters.GetValueOrDefault("color")?.ToString();
        if (string.IsNullOrWhiteSpace(colorStr))
            return SkillResult.Fail("Parameter 'color' is required.");

        var includeSurfaces = parameters.GetValueOrDefault("include_surfaces")?.ToString() != "false";
        _ = int.TryParse(parameters.GetValueOrDefault("transparency")?.ToString(), out var transparency);
        transparency = Math.Clamp(transparency, 0, 100);

        if (!TryParseColor(colorStr, out var r, out var g, out var b))
            return SkillResult.Fail($"Unknown color '{colorStr}'. Use a named color or hex #RRGGBB.");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var view = document.ActiveView;
            if (view is null) return new { overridden = 0, notFound = new List<string>() };

            var ids = ParseElementIds(idsStr);
            int overridden = 0;
            var notFound = new List<string>();

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

                var solidId = GetSolidFillPatternId(document);
                if (solidId != ElementId.InvalidElementId)
                {
                    ogs.SetSurfaceForegroundPatternId(solidId);
                    ogs.SetSurfaceBackgroundPatternId(solidId);
                }
            }

            if (transparency > 0)
                ogs.SetSurfaceTransparency(transparency);

            using var tx = new Transaction(document, "Override element colors");
            tx.Start();
            foreach (var id in ids)
            {
                var elemId = new ElementId(id);
                if (document.GetElement(elemId) is not null)
                {
                    view.SetElementOverrides(elemId, ogs);
                    overridden++;
                }
                else
                {
                    notFound.Add(id.ToString());
                }
            }
            tx.Commit();

            return new { overridden, notFound };
        });

        dynamic res = result!;
        int count = res.overridden;
        List<string> missing = res.notFound;

        var msg = $"Overrode color to {colorStr} on {count} element(s) in the active view.";
        if (missing.Count > 0)
            msg += $" Not found: {string.Join(", ", missing.Take(5))}" +
                   (missing.Count > 5 ? $" +{missing.Count - 5} more" : "");

        return SkillResult.Ok(msg, new { overridden = count, color = colorStr, transparency, notFound = missing });
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

    private static ElementId GetSolidFillPatternId(Document doc)
    {
        using var collector = new FilteredElementCollector(doc);
        var solidFill = collector
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
        return solidFill?.Id ?? ElementId.InvalidElementId;
    }

    private static List<long> ParseElementIds(string idsStr)
    {
        return idsStr
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s.Trim(), out var id) ? id : -1)
            .Where(id => id > 0)
            .ToList();
    }
}
