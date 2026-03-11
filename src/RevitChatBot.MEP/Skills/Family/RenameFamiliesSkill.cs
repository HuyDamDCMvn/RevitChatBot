using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Family;

[Skill("rename_families",
    "Batch rename family types in the Revit model. Supports adding prefix/suffix, " +
    "find-and-replace, or applying a naming convention. Inspired by DiRoots FamilyReviser. " +
    "Run with action='preview' first to review changes before applying.")]
[SkillParameter("action", "string",
    "'preview' to show proposed changes, 'apply' to rename.",
    isRequired: true,
    allowedValues: new[] { "preview", "apply" })]
[SkillParameter("operation", "string",
    "Rename operation: 'add_prefix', 'add_suffix', 'find_replace', 'remove_prefix', 'remove_suffix'.",
    isRequired: true,
    allowedValues: new[] { "add_prefix", "add_suffix", "find_replace", "remove_prefix", "remove_suffix" })]
[SkillParameter("value", "string",
    "The prefix/suffix text to add/remove, or the 'find' text for find_replace.",
    isRequired: true)]
[SkillParameter("replace_with", "string",
    "Replacement text for find_replace operation. Default empty (removes found text).",
    isRequired: false)]
[SkillParameter("category_filter", "string",
    "Limit to a specific category: 'ducts', 'pipes', 'equipment', 'electrical', 'plumbing', 'all'.",
    isRequired: false,
    allowedValues: new[] { "ducts", "pipes", "equipment", "electrical", "plumbing", "all" })]
[SkillParameter("name_filter", "string",
    "Only rename types whose current name contains this text.",
    isRequired: false)]
public class RenameFamiliesSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory[]> CategoryGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ducts"] = [BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_FlexDuctCurves, BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_DuctAccessory],
        ["pipes"] = [BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_FlexPipeCurves, BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory],
        ["equipment"] = [BuiltInCategory.OST_MechanicalEquipment],
        ["electrical"] = [BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures],
        ["plumbing"] = [BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_Sprinklers],
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "preview";
        var operation = parameters.GetValueOrDefault("operation")?.ToString() ?? "add_prefix";
        var value = parameters.GetValueOrDefault("value")?.ToString();
        var replaceWith = parameters.GetValueOrDefault("replace_with")?.ToString() ?? "";
        var catFilter = parameters.GetValueOrDefault("category_filter")?.ToString() ?? "all";
        var nameFilter = parameters.GetValueOrDefault("name_filter")?.ToString();

        if (string.IsNullOrWhiteSpace(value))
            return SkillResult.Fail("Parameter 'value' is required.");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var targetCats = GetTargetCategories(catFilter);
            var types = CollectFamilyTypes(document, targetCats, nameFilter);

            var changes = new List<RenameRecord>();
            foreach (var ft in types)
            {
                var oldName = ft.Name;
                var newName = ComputeNewName(oldName, operation, value, replaceWith);
                if (newName == oldName || string.IsNullOrWhiteSpace(newName)) continue;

                changes.Add(new RenameRecord
                {
                    TypeId = ft.Id.Value,
                    FamilyName = (ft as FamilySymbol)?.FamilyName ?? "",
                    OldName = oldName,
                    NewName = newName,
                    CategoryName = ft.Category?.Name ?? ""
                });
            }

            if (action == "apply" && changes.Count > 0)
            {
                var renamed = 0;
                var failed = 0;
                var failedNames = new List<string>();

                using var tx = new Transaction(document, "Batch rename family types");
                tx.Start();

                foreach (var change in changes)
                {
                    try
                    {
                        var elem = document.GetElement(new ElementId(change.TypeId));
                        if (elem is not null)
                        {
                            elem.Name = change.NewName;
                            renamed++;
                        }
                        else { failed++; failedNames.Add(change.OldName); }
                    }
                    catch
                    {
                        failed++;
                        failedNames.Add(change.OldName);
                    }
                }

                tx.Commit();

                return new
                {
                    action = "apply",
                    renamedCount = renamed,
                    failedCount = failed,
                    failedNames,
                    totalProposed = changes.Count,
                    details = changes.Take(50).ToList()
                };
            }

            return new
            {
                action = "preview",
                renamedCount = 0,
                failedCount = 0,
                failedNames = new List<string>(),
                totalProposed = changes.Count,
                details = changes.Take(50).ToList()
            };
        });

        dynamic res = result!;
        if (action == "apply")
            return SkillResult.Ok(
                $"Renamed {res.renamedCount} family types ({res.failedCount} failed).", result);

        return SkillResult.Ok(
            $"Preview: {res.totalProposed} types would be renamed. Run with action='apply' to execute.", result);
    }

    private static HashSet<long> GetTargetCategories(string catFilter)
    {
        if (catFilter == "all") return [];
        if (CategoryGroups.TryGetValue(catFilter, out var cats))
            return cats.Select(c => (long)c).ToHashSet();
        return [];
    }

    private static List<ElementType> CollectFamilyTypes(
        Document doc, HashSet<long> targetCats, string? nameFilter)
    {
        var types = new FilteredElementCollector(doc)
            .WhereElementIsElementType()
            .OfClass(typeof(FamilySymbol))
            .Cast<ElementType>()
            .ToList();

        if (targetCats.Count > 0)
            types = types.Where(t => t.Category is not null && targetCats.Contains(t.Category.Id.Value)).ToList();

        if (!string.IsNullOrWhiteSpace(nameFilter))
            types = types.Where(t => t.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        return types;
    }

    private static string ComputeNewName(string oldName, string operation, string value, string replaceWith)
    {
        return operation switch
        {
            "add_prefix" => value + oldName,
            "add_suffix" => oldName + value,
            "remove_prefix" => oldName.StartsWith(value, StringComparison.OrdinalIgnoreCase)
                ? oldName[value.Length..] : oldName,
            "remove_suffix" => oldName.EndsWith(value, StringComparison.OrdinalIgnoreCase)
                ? oldName[..^value.Length] : oldName,
            "find_replace" => Regex.Replace(oldName, Regex.Escape(value), replaceWith, RegexOptions.IgnoreCase),
            _ => oldName
        };
    }

    private class RenameRecord
    {
        public long TypeId { get; set; }
        public string FamilyName { get; set; } = "";
        public string OldName { get; set; } = "";
        public string NewName { get; set; } = "";
        public string CategoryName { get; set; } = "";
    }
}
