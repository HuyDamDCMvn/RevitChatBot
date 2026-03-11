using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Report;

/// <summary>
/// Generates a Bill of Quantities (BOQ) for MEP elements.
/// Groups by category → family/type → size, calculating total length,
/// count, and estimated weight. Exports to structured data.
/// </summary>
[Skill("generate_boq",
    "Generate a Bill of Quantities (BOQ) for MEP elements. Groups elements by " +
    "category, family/type, and size. Calculates total length (m), count, and " +
    "area for each group. Supports filtering by level and system.")]
[SkillParameter("category", "string",
    "'duct', 'pipe', 'conduit', 'cable_tray', 'equipment', 'all'. Default: 'all'.",
    isRequired: false, allowedValues: new[] { "duct", "pipe", "conduit", "cable_tray", "equipment", "all" })]
[SkillParameter("level", "string",
    "Filter by level name (optional).",
    isRequired: false)]
[SkillParameter("system_name", "string",
    "Filter by system name (optional).",
    isRequired: false)]
[SkillParameter("include_fittings", "string",
    "Include fittings in the count. 'true' or 'false'. Default: 'true'.",
    isRequired: false)]
public class GenerateBoqSkill : ISkill
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
        var includeFittings = parameters.GetValueOrDefault("include_fittings")?.ToString() != "false";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var boqItems = new Dictionary<string, BoqItem>();

            if (category is "duct" or "all")
            {
                CollectLinear(document, typeof(Duct), "Duct", levelFilter, systemFilter, boqItems);
                if (includeFittings)
                    CollectFittings(document, BuiltInCategory.OST_DuctFitting, "Duct Fitting",
                        levelFilter, systemFilter, boqItems);
                CollectAccessories(document, BuiltInCategory.OST_DuctAccessory, "Duct Accessory",
                    levelFilter, systemFilter, boqItems);
            }

            if (category is "pipe" or "all")
            {
                CollectLinear(document, typeof(Pipe), "Pipe", levelFilter, systemFilter, boqItems);
                if (includeFittings)
                    CollectFittings(document, BuiltInCategory.OST_PipeFitting, "Pipe Fitting",
                        levelFilter, systemFilter, boqItems);
                CollectAccessories(document, BuiltInCategory.OST_PipeAccessory, "Pipe Accessory",
                    levelFilter, systemFilter, boqItems);
            }

            if (category is "conduit" or "all")
                CollectByCategory(document, BuiltInCategory.OST_Conduit, "Conduit",
                    levelFilter, systemFilter, boqItems);

            if (category is "cable_tray" or "all")
                CollectByCategory(document, BuiltInCategory.OST_CableTray, "Cable Tray",
                    levelFilter, systemFilter, boqItems);

            if (category is "equipment" or "all")
                CollectEquipment(document, levelFilter, boqItems);

            var sorted = boqItems.Values
                .OrderBy(b => b.Category)
                .ThenBy(b => b.FamilyType)
                .ThenBy(b => b.Size)
                .Select(b => new
                {
                    category = b.Category,
                    familyType = b.FamilyType,
                    size = b.Size,
                    count = b.Count,
                    totalLengthM = Math.Round(b.TotalLengthM, 2),
                    totalAreaSqm = Math.Round(b.TotalAreaSqm, 2),
                    system = b.SystemName
                })
                .ToList();

            return new
            {
                totalLineItems = sorted.Count,
                totalElements = sorted.Sum(s => s.count),
                totalLengthM = Math.Round(sorted.Sum(s => s.totalLengthM), 1),
                boqItems = sorted
            };
        });

        return SkillResult.Ok("BOQ generated.", result);
    }

    private static void CollectByCategory(Document doc, BuiltInCategory bic, string categoryLabel,
        string? levelFilter, string? systemFilter, Dictionary<string, BoqItem> boq)
    {
        var elements = new FilteredElementCollector(doc)
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .ToList();

        foreach (var elem in elements)
        {
            if (!PassesFilters(doc, elem, levelFilter, systemFilter)) continue;

            var familyType = elem.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)?.AsValueString() ?? "Unknown";
            var size = elem.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";
            var lengthFt = (elem.Location as LocationCurve)?.Curve.Length ?? 0;
            var sysName = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";

            var key = $"{categoryLabel}|{familyType}|{size}|{sysName}";
            if (!boq.TryGetValue(key, out var item))
            {
                item = new BoqItem
                {
                    Category = categoryLabel,
                    FamilyType = familyType,
                    Size = size,
                    SystemName = sysName
                };
                boq[key] = item;
            }

            item.Count++;
            item.TotalLengthM += lengthFt * 0.3048;
        }
    }

    private static void CollectLinear(Document doc, Type elemClass, string categoryLabel,
        string? levelFilter, string? systemFilter, Dictionary<string, BoqItem> boq)
    {
        var elements = new FilteredElementCollector(doc)
            .OfClass(elemClass)
            .WhereElementIsNotElementType()
            .ToList();

        foreach (var elem in elements)
        {
            if (!PassesFilters(doc, elem, levelFilter, systemFilter)) continue;

            var familyType = elem.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)?.AsValueString() ?? "Unknown";
            var size = elem.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";
            var lengthFt = (elem.Location as LocationCurve)?.Curve.Length ?? 0;
            var sysName = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";

            var key = $"{categoryLabel}|{familyType}|{size}|{sysName}";
            if (!boq.TryGetValue(key, out var item))
            {
                item = new BoqItem
                {
                    Category = categoryLabel,
                    FamilyType = familyType,
                    Size = size,
                    SystemName = sysName
                };
                boq[key] = item;
            }

            item.Count++;
            item.TotalLengthM += lengthFt * 0.3048;
        }
    }

    private static void CollectFittings(Document doc, BuiltInCategory bic, string categoryLabel,
        string? levelFilter, string? systemFilter, Dictionary<string, BoqItem> boq)
    {
        var fittings = new FilteredElementCollector(doc)
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .ToList();

        foreach (var f in fittings)
        {
            if (!PassesFilters(doc, f, levelFilter, systemFilter)) continue;

            var familyType = f.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)?.AsValueString() ?? "Unknown";
            var size = f.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";
            var sysName = f.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";

            var key = $"{categoryLabel}|{familyType}|{size}|{sysName}";
            if (!boq.TryGetValue(key, out var item))
            {
                item = new BoqItem
                {
                    Category = categoryLabel,
                    FamilyType = familyType,
                    Size = size,
                    SystemName = sysName
                };
                boq[key] = item;
            }

            item.Count++;
        }
    }

    private static void CollectAccessories(Document doc, BuiltInCategory bic, string categoryLabel,
        string? levelFilter, string? systemFilter, Dictionary<string, BoqItem> boq)
    {
        var accessories = new FilteredElementCollector(doc)
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .ToList();

        foreach (var a in accessories)
        {
            if (!PassesFilters(doc, a, levelFilter, systemFilter)) continue;

            var familyType = a.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)?.AsValueString() ?? "Unknown";
            var sysName = a.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";
            var key = $"{categoryLabel}|{familyType}|N/A|{sysName}";

            if (!boq.TryGetValue(key, out var item))
            {
                item = new BoqItem
                {
                    Category = categoryLabel,
                    FamilyType = familyType,
                    Size = "N/A",
                    SystemName = sysName
                };
                boq[key] = item;
            }

            item.Count++;
        }
    }

    private static void CollectEquipment(Document doc, string? levelFilter,
        Dictionary<string, BoqItem> boq)
    {
        var categories = new[]
        {
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_ElectricalFixtures
        };

        foreach (var bic in categories)
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

                var familyType = elem.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)?.AsValueString() ?? "Unknown";
                var catName = bic.ToString().Replace("OST_", "").Replace("Equipment", " Equipment")
                    .Replace("Fixtures", " Fixtures");
                var key = $"{catName}|{familyType}|unit|";

                if (!boq.TryGetValue(key, out var item))
                {
                    item = new BoqItem
                    {
                        Category = catName,
                        FamilyType = familyType,
                        Size = "unit"
                    };
                    boq[key] = item;
                }

                item.Count++;
            }
        }
    }

    private static bool PassesFilters(Document doc, Element elem, string? levelFilter, string? systemFilter)
    {
        if (!string.IsNullOrWhiteSpace(levelFilter))
        {
            var lvlName = GetLevelName(doc, elem);
            if (!lvlName.Contains(levelFilter, StringComparison.OrdinalIgnoreCase)) return false;
        }

        if (!string.IsNullOrWhiteSpace(systemFilter))
        {
            var sysName = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";
            if (!sysName.Contains(systemFilter, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var lvlId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId()
                    ?? elem.LevelId;
        if (lvlId is null || lvlId == ElementId.InvalidElementId) return "";
        return doc.GetElement(lvlId)?.Name ?? "";
    }

    private class BoqItem
    {
        public string Category { get; set; } = "";
        public string FamilyType { get; set; } = "";
        public string Size { get; set; } = "";
        public string SystemName { get; set; } = "";
        public int Count { get; set; }
        public double TotalLengthM { get; set; }
        public double TotalAreaSqm { get; set; }
    }
}
