using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Modify;

/// <summary>
/// General-purpose element copy between levels. Copies elements from source level to
/// target level, adjusting elevation while preserving horizontal position.
/// Supports filtering by category, element IDs, or current selection.
/// </summary>
[Skill("copy_elements_to_level",
    "Copy elements from one level to another, preserving horizontal position and " +
    "adjusting elevation. Supports ducts, pipes, fittings, equipment, accessories, " +
    "cable trays, conduits. Returns new element IDs for chaining.")]
[SkillParameter("source_level", "string",
    "Source level name to copy FROM (e.g. 'Level 1').",
    isRequired: true)]
[SkillParameter("target_level", "string",
    "Target level name to copy TO (e.g. 'Level 2').",
    isRequired: true)]
[SkillParameter("category", "string",
    "Category filter: 'all_mep', 'ducts', 'pipes', 'fittings', 'equipment', " +
    "'cable_trays', 'conduits', 'accessories'. Default: 'all_mep'.",
    isRequired: false,
    allowedValues: new[] { "all_mep", "ducts", "pipes", "fittings", "equipment",
        "cable_trays", "conduits", "accessories" })]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs to copy (overrides category filter). Optional.",
    isRequired: false)]
[SkillParameter("offset_x_mm", "number",
    "Additional horizontal X offset in mm. Default: 0.",
    isRequired: false)]
[SkillParameter("offset_y_mm", "number",
    "Additional horizontal Y offset in mm. Default: 0.",
    isRequired: false)]
public class CopyElementsToLevelSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory[]> CategoryGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["all_mep"] = [
            BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_Conduit, BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_PipeAccessory
        ],
        ["ducts"] = [BuiltInCategory.OST_DuctCurves],
        ["pipes"] = [BuiltInCategory.OST_PipeCurves],
        ["fittings"] = [BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_PipeFitting],
        ["equipment"] = [BuiltInCategory.OST_MechanicalEquipment],
        ["cable_trays"] = [BuiltInCategory.OST_CableTray],
        ["conduits"] = [BuiltInCategory.OST_Conduit],
        ["accessories"] = [BuiltInCategory.OST_DuctAccessory, BuiltInCategory.OST_PipeAccessory],
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var sourceLevelName = parameters.GetValueOrDefault("source_level")?.ToString();
        var targetLevelName = parameters.GetValueOrDefault("target_level")?.ToString();
        if (string.IsNullOrWhiteSpace(sourceLevelName) || string.IsNullOrWhiteSpace(targetLevelName))
            return SkillResult.Fail("Both 'source_level' and 'target_level' are required.");

        var categoryStr = parameters.GetValueOrDefault("category")?.ToString() ?? "all_mep";
        var elementIdsStr = parameters.GetValueOrDefault("element_ids")?.ToString();
        var offsetXMm = ParseDouble(parameters.GetValueOrDefault("offset_x_mm"), 0);
        var offsetYMm = ParseDouble(parameters.GetValueOrDefault("offset_y_mm"), 0);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var sourceLevel = FindLevel(document, sourceLevelName!);
            var targetLevel = FindLevel(document, targetLevelName!);
            if (sourceLevel is null)
                return new { error = $"Source level '{sourceLevelName}' not found." };
            if (targetLevel is null)
                return new { error = $"Target level '{targetLevelName}' not found." };

            var elevDiffFt = targetLevel.Elevation - sourceLevel.Elevation;
            var offsetXFt = offsetXMm / 304.8;
            var offsetYFt = offsetYMm / 304.8;
            var translation = new XYZ(offsetXFt, offsetYFt, elevDiffFt);

            ICollection<ElementId> sourceIds;
            if (!string.IsNullOrWhiteSpace(elementIdsStr))
            {
                sourceIds = elementIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => long.TryParse(s.Trim(), out var id) ? new ElementId(id) : null)
                    .Where(id => id is not null && id != ElementId.InvalidElementId)
                    .ToList()!;
            }
            else
            {
                sourceIds = CollectElements(document, categoryStr, sourceLevel.Id);
            }

            if (sourceIds.Count == 0)
                return new { error = "No elements found on source level matching criteria." };

            List<ElementId> newIds;
            using (var tx = new Transaction(document, "Copy elements to level"))
            {
                tx.Start();
                try
                {
                    newIds = ElementTransformUtils.CopyElements(
                        document, sourceIds, document, Transform.CreateTranslation(translation), new CopyPasteOptions())
                        .ToList();

                    foreach (var newId in newIds)
                    {
                        var elem = document.GetElement(newId);
                        if (elem is null) continue;

                        var levelParam = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)
                                         ?? elem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                        if (levelParam is { IsReadOnly: false })
                            levelParam.Set(targetLevel.Id);
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    return new { error = $"Copy failed: {ex.Message}" };
                }
            }

            return new
            {
                sourceLevel = sourceLevel.Name,
                targetLevel = targetLevel.Name,
                sourceCount = sourceIds.Count,
                copiedCount = newIds.Count,
                newElementIds = newIds.Select(id => id.Value).ToList(),
                elevationOffsetM = Math.Round(elevDiffFt * 0.3048, 2)
            };
        });

        dynamic res = result!;
        if (res is not null && ((IDictionary<string, object?>)res).ContainsKey("error"))
            return SkillResult.Fail(res.error?.ToString() ?? "Copy failed.");

        return SkillResult.Ok("Elements copied to target level.", result);
    }

    private static Level? FindLevel(Document doc, string name)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static ICollection<ElementId> CollectElements(Document doc, string category, ElementId sourceLevelId)
    {
        if (!CategoryGroups.TryGetValue(category, out var bics))
            bics = CategoryGroups["all_mep"];

        var ids = new List<ElementId>();
        foreach (var bic in bics)
        {
            var elems = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    var lid = e.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId()
                              ?? e.LevelId;
                    return lid is not null && lid == sourceLevelId;
                })
                .Select(e => e.Id);
            ids.AddRange(elems);
        }
        return ids;
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
