using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Modify;

/// <summary>
/// Move/offset elements by a specified distance. Supports moving vertically (elevation change),
/// horizontally, or in any direction. Works with element IDs, category filter, or level filter.
/// Very common daily operation for MEP modelers adjusting pipe/duct elevations.
/// </summary>
[Skill("move_elements",
    "Move or offset elements by a specified distance. Common use: adjust duct/pipe elevation, " +
    "shift equipment position, offset elements for coordination. " +
    "Supports vertical (Z), horizontal (X/Y), or combined offset.")]
[SkillParameter("offset_z_mm", "number",
    "Vertical offset in mm. Positive = up, negative = down. Default: 0.",
    isRequired: false)]
[SkillParameter("offset_x_mm", "number",
    "X-axis offset in mm. Default: 0.",
    isRequired: false)]
[SkillParameter("offset_y_mm", "number",
    "Y-axis offset in mm. Default: 0.",
    isRequired: false)]
[SkillParameter("source", "string",
    "Source: 'element_ids' or 'filter'. Default: 'element_ids'.",
    isRequired: false, allowedValues: new[] { "element_ids", "filter" })]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs to move (when source='element_ids').",
    isRequired: false)]
[SkillParameter("category", "string",
    "Category filter: 'ducts', 'pipes', 'fittings', 'equipment', 'cable_trays', 'conduits'.",
    isRequired: false)]
[SkillParameter("level", "string",
    "Level name filter (when source='filter'). Optional.",
    isRequired: false)]
[SkillParameter("system_name", "string",
    "System name filter (when source='filter'). Optional.",
    isRequired: false)]
public class MoveElementsSkill : ISkill
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
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var offsetX = ParseDouble(parameters.GetValueOrDefault("offset_x_mm"), 0) / 304.8;
        var offsetY = ParseDouble(parameters.GetValueOrDefault("offset_y_mm"), 0) / 304.8;
        var offsetZ = ParseDouble(parameters.GetValueOrDefault("offset_z_mm"), 0) / 304.8;

        if (Math.Abs(offsetX) < 1e-9 && Math.Abs(offsetY) < 1e-9 && Math.Abs(offsetZ) < 1e-9)
            return SkillResult.Fail("At least one offset (X, Y, or Z) must be non-zero.");

        var source = parameters.GetValueOrDefault("source")?.ToString() ?? "element_ids";
        var elementIdsStr = parameters.GetValueOrDefault("element_ids")?.ToString();
        var categoryStr = parameters.GetValueOrDefault("category")?.ToString();
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var systemFilter = parameters.GetValueOrDefault("system_name")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var translation = new XYZ(offsetX, offsetY, offsetZ);

            List<ElementId> ids;
            if (source == "element_ids" && !string.IsNullOrWhiteSpace(elementIdsStr))
            {
                ids = elementIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => long.TryParse(s.Trim(), out var id) ? new ElementId(id) : null)
                    .Where(id => id is not null && id != ElementId.InvalidElementId)
                    .ToList()!;
            }
            else if (!string.IsNullOrWhiteSpace(categoryStr))
            {
                var collector = CategoryMap.TryGetValue(categoryStr, out var bic)
                    ? new FluentCollector(document).OfCategory(bic).WhereElementIsNotElementType()
                    : new FluentCollector(document).WhereElementIsNotElementType();

                if (!string.IsNullOrWhiteSpace(levelFilter))
                    collector.OnLevel(levelFilter);
                if (!string.IsNullOrWhiteSpace(systemFilter))
                    collector.InSystem(systemFilter);

                ids = collector.ToList().Select(e => e.Id).ToList();
            }
            else
            {
                return new { error = "Specify 'element_ids' or 'category' filter." };
            }

            if (ids.Count == 0)
                return new { error = "No elements found matching criteria." };

            int moved = 0, failed = 0;
            using var tx = new Transaction(document, "Move elements");
            tx.Start();
            try
            {
                foreach (var id in ids)
                {
                    try
                    {
                        ElementTransformUtils.MoveElement(document, id, translation);
                        moved++;
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
                totalElements = ids.Count,
                moved,
                failed,
                offsetMm = new { x = Math.Round(offsetX * 304.8), y = Math.Round(offsetY * 304.8), z = Math.Round(offsetZ * 304.8) }
            };
        });

        dynamic res = result!;
        if (((IDictionary<string, object>)res).ContainsKey("error"))
            return SkillResult.Fail(res.error?.ToString() ?? "Move failed.");

        return SkillResult.Ok($"Moved {res.moved} elements.", result);
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
