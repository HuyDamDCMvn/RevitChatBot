using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Report;

/// <summary>
/// Extracts structured data for generating MEP riser diagrams.
/// Returns system routing between levels: sizes, connections, equipment,
/// and vertical penetrations organized floor-by-floor.
/// </summary>
[Skill("generate_riser_diagram_data",
    "Extract structured data for MEP riser diagrams. Returns system routing between " +
    "levels including pipe/duct sizes, vertical risers, equipment connections, " +
    "and floor penetrations organized floor-by-floor for diagram generation.")]
[SkillParameter("system_type", "string",
    "'duct', 'pipe', or 'all'. Default: 'all'.",
    isRequired: false, allowedValues: new[] { "duct", "pipe", "all" })]
[SkillParameter("system_name", "string",
    "Filter by specific system name (optional).",
    isRequired: false)]
public class GenerateRiserDiagramDataSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var systemType = parameters.GetValueOrDefault("system_type")?.ToString() ?? "all";
        var systemNameFilter = parameters.GetValueOrDefault("system_name")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var levels = new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            var verticalElements = new List<Element>();

            if (systemType is "duct" or "all")
            {
                verticalElements.AddRange(new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType()
                    .Where(e => IsVertical(e))
                    .ToList());
            }

            if (systemType is "pipe" or "all")
            {
                verticalElements.AddRange(new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType()
                    .Where(e => IsVertical(e))
                    .ToList());
            }

            if (!string.IsNullOrWhiteSpace(systemNameFilter))
                verticalElements = verticalElements.Where(e =>
                    (e.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "")
                        .Contains(systemNameFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            var equipment = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                .WhereElementIsNotElementType()
                .ToList();

            if (!string.IsNullOrWhiteSpace(systemNameFilter))
                equipment = equipment.Where(e =>
                    (e.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "")
                        .Contains(systemNameFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            var levelData = levels.Select(level =>
            {
                var risersAtLevel = verticalElements
                    .Where(e =>
                    {
                        var bb = e.get_BoundingBox(null);
                        return bb is not null &&
                               bb.Min.Z <= level.Elevation + 1 &&
                               bb.Max.Z >= level.Elevation - 1;
                    })
                    .Select(e => new
                    {
                        elementId = e.Id.Value,
                        category = e.Category?.Name ?? "Unknown",
                        size = e.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A",
                        system = e.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "N/A",
                        classification = e.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? ""
                    })
                    .ToList();

                var equipAtLevel = equipment
                    .Where(e =>
                    {
                        var lvlId = e.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId() ?? e.LevelId;
                        return lvlId == level.Id;
                    })
                    .Select(e => new
                    {
                        elementId = e.Id.Value,
                        family = e.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString() ?? "N/A",
                        mark = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? ""
                    })
                    .ToList();

                return new
                {
                    levelName = level.Name,
                    elevationM = Math.Round(level.Elevation * 0.3048, 2),
                    risers = risersAtLevel,
                    equipment = equipAtLevel,
                    riserCount = risersAtLevel.Count,
                    equipmentCount = equipAtLevel.Count
                };
            }).ToList();

            var systemSummary = verticalElements
                .GroupBy(e => e.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "Unassigned")
                .Select(g => new
                {
                    systemName = g.Key,
                    riserCount = g.Count(),
                    classification = g.First().get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "",
                    sizes = g.Select(e => e.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A")
                        .Distinct().ToList(),
                    spanLevels = g.Count()
                })
                .OrderByDescending(s => s.riserCount)
                .ToList();

            return new
            {
                totalLevels = levels.Count,
                totalVerticalRisers = verticalElements.Count,
                totalEquipment = equipment.Count,
                levels = levelData,
                systemSummary
            };
        });

        return SkillResult.Ok("Riser diagram data extracted.", result);
    }

    private static bool IsVertical(Element elem)
    {
        if (elem.Location is not LocationCurve lc) return false;
        var curve = lc.Curve;
        var start = curve.GetEndPoint(0);
        var end = curve.GetEndPoint(1);
        var dz = Math.Abs(end.Z - start.Z);
        var dxy = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
        return dz > dxy * 2;
    }
}
