using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Family;

[Skill("audit_families",
    "Audit loaded families for naming conventions, parameter completeness, and usage. " +
    "Detects issues like non-standard names, missing shared parameters, " +
    "and oversized families. Inspired by DiRoots FamilyReviser.")]
[SkillParameter("category_filter", "string",
    "Category to audit: 'ducts', 'pipes', 'equipment', 'electrical', 'plumbing', 'all'. Default 'all'.",
    isRequired: false,
    allowedValues: new[] { "ducts", "pipes", "equipment", "electrical", "plumbing", "all" })]
[SkillParameter("naming_convention", "string",
    "Expected naming pattern. Uses simple rules: " +
    "'prefix:MEP_' checks that names start with 'MEP_', " +
    "'no_spaces' flags names with spaces, " +
    "'no_special' flags special characters. Default: general audit.",
    isRequired: false)]
[SkillParameter("check_parameters", "string",
    "Comma-separated parameter names that every family should have (e.g. 'Mark,Comments,Description').",
    isRequired: false)]
public class AuditFamiliesSkill : ISkill
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

        var catFilter = parameters.GetValueOrDefault("category_filter")?.ToString() ?? "all";
        var namingConvention = parameters.GetValueOrDefault("naming_convention")?.ToString();
        var checkParams = parameters.GetValueOrDefault("check_parameters")?.ToString();
        var requiredParams = string.IsNullOrWhiteSpace(checkParams)
            ? new List<string>()
            : checkParams.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var targetCats = GetTargetCategories(catFilter);

            var families = new FilteredElementCollector(document)
                .OfClass(typeof(Autodesk.Revit.DB.Family))
                .Cast<Autodesk.Revit.DB.Family>()
                .Where(f =>
                {
                    if (targetCats.Count == 0) return true;
                    return f.FamilyCategory is not null && targetCats.Contains(f.FamilyCategory.Id.Value);
                })
                .ToList();

            var allInstances = new FilteredElementCollector(document)
                .WhereElementIsNotElementType()
                .ToElements();

            var usedTypeIds = new HashSet<long>();
            foreach (var inst in allInstances)
            {
                var tid = inst.GetTypeId();
                if (tid != ElementId.InvalidElementId)
                    usedTypeIds.Add(tid.Value);
            }

            var issues = new List<AuditIssue>();
            var familySummaries = new List<FamilySummary>();

            foreach (var family in families)
            {
                var typeCount = 0;
                var usedTypeCount = 0;
                var instanceCount = 0;

                foreach (var symId in family.GetFamilySymbolIds())
                {
                    typeCount++;
                    if (usedTypeIds.Contains(symId.Value))
                    {
                        usedTypeCount++;
                        instanceCount += new FilteredElementCollector(document)
                            .WhereElementIsNotElementType()
                            .Where(e => e.GetTypeId() == symId)
                            .Count();
                    }
                }

                familySummaries.Add(new FamilySummary
                {
                    FamilyName = family.Name,
                    Category = family.FamilyCategory?.Name ?? "",
                    TypeCount = typeCount,
                    UsedTypeCount = usedTypeCount,
                    InstanceCount = instanceCount
                });

                CheckNamingConvention(family, namingConvention, issues);
                CheckRequiredParameters(document, family, requiredParams, issues);

                if (usedTypeCount == 0 && typeCount > 0)
                {
                    issues.Add(new AuditIssue
                    {
                        FamilyName = family.Name,
                        Severity = "warning",
                        Issue = "Completely unused family (0 placed instances)."
                    });
                }

                if (typeCount > 10)
                {
                    issues.Add(new AuditIssue
                    {
                        FamilyName = family.Name,
                        Severity = "info",
                        Issue = $"High type count ({typeCount} types). Consider consolidating."
                    });
                }
            }

            var issuesBySeverity = issues
                .GroupBy(i => i.Severity)
                .Select(g => new { severity = g.Key, count = g.Count() })
                .ToList();

            return new
            {
                totalFamilies = families.Count,
                totalIssues = issues.Count,
                issuesBySeverity,
                issues = issues.Take(100).ToList(),
                topFamiliesByTypes = familySummaries
                    .OrderByDescending(f => f.TypeCount)
                    .Take(20)
                    .ToList(),
                unusedFamilies = familySummaries
                    .Where(f => f.InstanceCount == 0)
                    .Take(20)
                    .ToList()
            };
        });

        dynamic res = result!;
        return SkillResult.Ok(
            $"Audited {res.totalFamilies} families — found {res.totalIssues} issues.", result);
    }

    private static void CheckNamingConvention(Autodesk.Revit.DB.Family family, string? convention, List<AuditIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(convention)) return;

        var name = family.Name;

        if (convention.StartsWith("prefix:", StringComparison.OrdinalIgnoreCase))
        {
            var prefix = convention[7..];
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new AuditIssue
                {
                    FamilyName = name,
                    Severity = "warning",
                    Issue = $"Name does not start with required prefix '{prefix}'."
                });
            }
        }
        else if (convention == "no_spaces" && name.Contains(' '))
        {
            issues.Add(new AuditIssue
            {
                FamilyName = name,
                Severity = "warning",
                Issue = "Name contains spaces."
            });
        }
        else if (convention == "no_special")
        {
            if (name.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != ' '))
            {
                issues.Add(new AuditIssue
                {
                    FamilyName = name,
                    Severity = "warning",
                    Issue = "Name contains special characters."
                });
            }
        }
    }

    private static void CheckRequiredParameters(
        Document doc, Autodesk.Revit.DB.Family family, List<string> requiredParams, List<AuditIssue> issues)
    {
        if (requiredParams.Count == 0) return;

        var firstSymbolId = family.GetFamilySymbolIds().FirstOrDefault();
        if (firstSymbolId is null || firstSymbolId == ElementId.InvalidElementId) return;

        var symbol = doc.GetElement(firstSymbolId);
        if (symbol is null) return;

        foreach (var paramName in requiredParams)
        {
            if (symbol.LookupParameter(paramName) is null)
            {
                issues.Add(new AuditIssue
                {
                    FamilyName = family.Name,
                    Severity = "error",
                    Issue = $"Missing required parameter '{paramName}'."
                });
            }
        }
    }

    private static HashSet<long> GetTargetCategories(string catFilter)
    {
        if (catFilter == "all") return [];
        if (CategoryGroups.TryGetValue(catFilter, out var cats))
            return cats.Select(c => (long)c).ToHashSet();
        return [];
    }

    private class AuditIssue
    {
        public string FamilyName { get; set; } = "";
        public string Severity { get; set; } = "info";
        public string Issue { get; set; } = "";
    }

    private class FamilySummary
    {
        public string FamilyName { get; set; } = "";
        public string Category { get; set; } = "";
        public int TypeCount { get; set; }
        public int UsedTypeCount { get; set; }
        public int InstanceCount { get; set; }
    }
}
