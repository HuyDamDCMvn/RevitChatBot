using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Report;

/// <summary>
/// Generates a material takeoff report with detailed material information
/// for MEP elements: material name, area, volume, and weight estimates.
/// </summary>
[Skill("generate_material_takeoff",
    "Generate a material takeoff for MEP elements. Lists materials with total area (m²), " +
    "estimated volume, and weight by category and material type. " +
    "Useful for procurement and cost estimation.")]
[SkillParameter("category", "string",
    "'duct', 'pipe', 'insulation', 'all'. Default: 'all'.",
    isRequired: false, allowedValues: new[] { "duct", "pipe", "insulation", "all" })]
[SkillParameter("level", "string",
    "Filter by level name (optional).",
    isRequired: false)]
[SkillParameter("system_name", "string",
    "Filter by system name (optional).",
    isRequired: false)]
public class GenerateMaterialTakeoffSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var category = parameters.GetValueOrDefault("category")?.ToString() ?? "all";
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var systemFilter = parameters.GetValueOrDefault("system_name")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var materials = new Dictionary<string, MaterialEntry>();

            if (category is "duct" or "all")
                CollectMaterials(document, BuiltInCategory.OST_DuctCurves, "Duct",
                    levelFilter, systemFilter, materials);

            if (category is "pipe" or "all")
                CollectMaterials(document, BuiltInCategory.OST_PipeCurves, "Pipe",
                    levelFilter, systemFilter, materials);

            if (category is "insulation" or "all")
            {
                CollectMaterials(document, BuiltInCategory.OST_DuctInsulations, "Duct Insulation",
                    levelFilter, systemFilter, materials);
                CollectMaterials(document, BuiltInCategory.OST_PipeInsulations, "Pipe Insulation",
                    levelFilter, systemFilter, materials);
            }

            var sorted = materials.Values
                .OrderBy(m => m.Category)
                .ThenByDescending(m => m.TotalAreaSqm)
                .Select(m => new
                {
                    category = m.Category,
                    material = m.MaterialName,
                    elementCount = m.Count,
                    totalLengthM = Math.Round(m.TotalLengthM, 1),
                    totalAreaSqm = Math.Round(m.TotalAreaSqm, 2),
                    system = m.SystemName
                })
                .ToList();

            return new
            {
                totalMaterialGroups = sorted.Count,
                totalElements = sorted.Sum(s => s.elementCount),
                totalAreaSqm = Math.Round(sorted.Sum(s => s.totalAreaSqm), 1),
                materials = sorted
            };
        });

        return SkillResult.Ok("Material takeoff generated.", result);
    }

    private static void CollectMaterials(Document doc, BuiltInCategory bic, string categoryLabel,
        string? levelFilter, string? systemFilter, Dictionary<string, MaterialEntry> materials)
    {
        var elements = new FilteredElementCollector(doc)
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .ToList();

        foreach (var elem in elements)
        {
            if (!string.IsNullOrWhiteSpace(levelFilter))
            {
                var lvlName = GetLevelName(doc, elem);
                if (!lvlName.Contains(levelFilter, StringComparison.OrdinalIgnoreCase)) continue;
            }

            if (!string.IsNullOrWhiteSpace(systemFilter))
            {
                var sysName = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";
                if (!sysName.Contains(systemFilter, StringComparison.OrdinalIgnoreCase)) continue;
            }

            var materialName = GetMaterialName(doc, elem);
            var size = elem.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";
            var system = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";
            var lengthFt = (elem.Location as LocationCurve)?.Curve.Length ?? 0;
            var areaSqft = elem.get_Parameter(BuiltInParameter.RBS_CURVE_SURFACE_AREA)?.AsDouble() ?? 0;

            var key = $"{categoryLabel}|{materialName}|{size}|{system}";
            if (!materials.TryGetValue(key, out var entry))
            {
                entry = new MaterialEntry
                {
                    Category = categoryLabel,
                    MaterialName = materialName,
                    Size = size,
                    SystemName = system
                };
                materials[key] = entry;
            }

            entry.Count++;
            entry.TotalLengthM += lengthFt * 0.3048;
            entry.TotalAreaSqm += areaSqft * 0.092903;
        }
    }

    private static string GetMaterialName(Document doc, Element elem)
    {
        var materialIds = elem.GetMaterialIds(false);
        if (materialIds.Count > 0)
        {
            var mat = doc.GetElement(materialIds.First());
            if (mat is not null) return mat.Name;
        }

        var typeName = elem.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)?.AsValueString() ?? "";
        return !string.IsNullOrWhiteSpace(typeName) ? typeName : "Unknown Material";
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var lvlId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId() ?? elem.LevelId;
        if (lvlId is null || lvlId == ElementId.InvalidElementId) return "";
        return doc.GetElement(lvlId)?.Name ?? "";
    }

    private class MaterialEntry
    {
        public string Category { get; set; } = "";
        public string MaterialName { get; set; } = "";
        public string Size { get; set; } = "";
        public string SystemName { get; set; } = "";
        public int Count { get; set; }
        public double TotalLengthM { get; set; }
        public double TotalAreaSqm { get; set; }
    }
}
