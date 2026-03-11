using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Cleanup;

[Skill("cleanup_unused_families",
    "Audit and optionally purge unused family types from the Revit model. " +
    "Similar to Revit's Purge Unused but smarter — groups by category and shows size impact. " +
    "First run with action='audit' to preview, then 'delete' to purge.")]
[SkillParameter("action", "string",
    "'audit' to list unused families/types without deleting, 'delete' to purge them.",
    isRequired: true,
    allowedValues: new[] { "audit", "delete" })]
[SkillParameter("category_filter", "string",
    "Limit cleanup to a specific category: 'ducts', 'pipes', 'equipment', 'fittings', 'all'.",
    isRequired: false,
    allowedValues: new[] { "ducts", "pipes", "equipment", "fittings", "electrical", "plumbing", "all" })]
public class CleanupUnusedFamiliesSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory[]> CategoryGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ducts"] = [BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_FlexDuctCurves, BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_DuctAccessory],
        ["pipes"] = [BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_FlexPipeCurves, BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory],
        ["equipment"] = [BuiltInCategory.OST_MechanicalEquipment],
        ["fittings"] = [BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_PipeFitting],
        ["electrical"] = [BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_CableTray, BuiltInCategory.OST_Conduit],
        ["plumbing"] = [BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_Sprinklers],
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "audit";
        var categoryFilter = parameters.GetValueOrDefault("category_filter")?.ToString() ?? "all";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var unusedTypes = FindUnusedFamilyTypes(document, categoryFilter);

            if (action == "delete" && unusedTypes.Count > 0)
            {
                var deletedCount = 0;
                var failedCount = 0;

                using var tx = new Transaction(document, "Purge unused family types");
                tx.Start();

                foreach (var info in unusedTypes)
                {
                    try
                    {
                        document.Delete(new ElementId(info.TypeId));
                        deletedCount++;
                    }
                    catch
                    {
                        failedCount++;
                    }
                }

                tx.Commit();

                return new
                {
                    action = "delete",
                    deletedCount,
                    failedCount,
                    totalFound = unusedTypes.Count,
                    details = unusedTypes
                };
            }

            var grouped = unusedTypes
                .GroupBy(u => u.CategoryName)
                .Select(g => new { category = g.Key, count = g.Count() })
                .OrderByDescending(g => g.count)
                .ToList();

            return new
            {
                action = "audit",
                deletedCount = 0,
                failedCount = 0,
                totalFound = unusedTypes.Count,
                byCategory = grouped,
                details = unusedTypes.Take(50).ToList()
            };
        });

        dynamic res = result!;
        if (action == "delete")
            return SkillResult.Ok(
                $"Purged {res.deletedCount} unused family types ({res.failedCount} failed).", result);

        return SkillResult.Ok(
            $"Found {res.totalFound} unused family types. Run with action='delete' to purge.", result);
    }

    private static List<UnusedTypeInfo> FindUnusedFamilyTypes(Document doc, string categoryFilter)
    {
        var targetCategories = new HashSet<long>();
        if (categoryFilter != "all" && CategoryGroups.TryGetValue(categoryFilter, out var cats))
        {
            foreach (var c in cats) targetCategories.Add((long)c);
        }

        var allInstances = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .ToElements();

        var usedTypeIds = new HashSet<long>();
        foreach (var inst in allInstances)
        {
            var typeId = inst.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
                usedTypeIds.Add(typeId.Value);
        }

        var allTypes = new FilteredElementCollector(doc)
            .WhereElementIsElementType()
            .ToElements();

        var unused = new List<UnusedTypeInfo>();
        foreach (var typeElem in allTypes)
        {
            if (usedTypeIds.Contains(typeElem.Id.Value)) continue;
            if (typeElem.Category is null) continue;

            if (targetCategories.Count > 0 && !targetCategories.Contains(typeElem.Category.Id.Value))
                continue;

            unused.Add(new UnusedTypeInfo
            {
                TypeId = typeElem.Id.Value,
                TypeName = typeElem.Name,
                FamilyName = (typeElem as FamilySymbol)?.FamilyName ?? typeElem.Name,
                CategoryName = typeElem.Category.Name
            });
        }

        return unused;
    }

    private class UnusedTypeInfo
    {
        public long TypeId { get; set; }
        public string TypeName { get; set; } = "";
        public string FamilyName { get; set; } = "";
        public string CategoryName { get; set; } = "";
    }
}
