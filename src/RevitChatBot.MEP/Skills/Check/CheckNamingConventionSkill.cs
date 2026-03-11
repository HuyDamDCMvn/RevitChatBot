using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Check;

/// <summary>
/// Checks that views, families, types, and systems follow naming conventions.
/// Supports configurable regex patterns per category.
/// </summary>
[Skill("check_naming_convention",
    "Check naming conventions across the model. Validates that views, families, " +
    "types, systems, and levels follow consistent naming patterns. " +
    "Reports non-conforming names with suggestions.")]
[SkillParameter("scope", "string",
    "'views', 'families', 'systems', 'levels', 'all'. Default: 'all'.",
    isRequired: false, allowedValues: new[] { "views", "families", "systems", "levels", "all" })]
[SkillParameter("pattern", "string",
    "Optional regex pattern to enforce. If omitted, checks for common issues " +
    "(spaces, special characters, inconsistent prefixes).",
    isRequired: false)]
public class CheckNamingConventionSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var scope = parameters.GetValueOrDefault("scope")?.ToString() ?? "all";
        var pattern = parameters.GetValueOrDefault("pattern")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var sections = new List<object>();

            if (scope is "views" or "all")
                sections.Add(CheckViews(document, pattern));

            if (scope is "families" or "all")
                sections.Add(CheckFamilies(document, pattern));

            if (scope is "systems" or "all")
                sections.Add(CheckSystems(document, pattern));

            if (scope is "levels" or "all")
                sections.Add(CheckLevels(document, pattern));

            int totalIssues = sections.Sum(s => ((dynamic)s).issueCount);
            return new
            {
                overallStatus = totalIssues > 0 ? "NAMING ISSUES FOUND" : "OK",
                totalIssues,
                sections
            };
        });

        return SkillResult.Ok("Naming convention check completed.", result);
    }

    private static object CheckViews(Document doc, string? pattern)
    {
        var views = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate)
            .ToList();

        var issues = new List<object>();
        foreach (var view in views)
        {
            var name = view.Name;
            var problems = FindNamingIssues(name, pattern);
            if (problems.Count > 0)
            {
                issues.Add(new
                {
                    name,
                    viewType = view.ViewType.ToString(),
                    problems
                });
            }
        }

        return new
        {
            category = "Views",
            totalChecked = views.Count,
            issueCount = issues.Count,
            issues = issues.Take(20).ToList()
        };
    }

    private static object CheckFamilies(Document doc, string? pattern)
    {
        var types = new FilteredElementCollector(doc)
            .WhereElementIsElementType()
            .Where(t =>
            {
                var cat = t.Category?.BuiltInCategory;
                return cat == BuiltInCategory.OST_MechanicalEquipment ||
                       cat == BuiltInCategory.OST_DuctCurves ||
                       cat == BuiltInCategory.OST_PipeCurves ||
                       cat == BuiltInCategory.OST_ElectricalEquipment ||
                       cat == BuiltInCategory.OST_PlumbingFixtures;
            })
            .ToList();

        var issues = new List<object>();
        var familyNames = new HashSet<string>();
        int duplicateNames = 0;

        foreach (var type in types)
        {
            var familyName = type.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME)?.AsString() ?? "";
            var typeName = type.Name;
            var fullName = $"{familyName}: {typeName}";

            if (!familyNames.Add(fullName))
            {
                duplicateNames++;
                continue;
            }

            var problems = FindNamingIssues(familyName, pattern);
            problems.AddRange(FindNamingIssues(typeName, pattern));

            if (problems.Count > 0)
            {
                issues.Add(new
                {
                    familyName,
                    typeName,
                    category = type.Category?.Name ?? "N/A",
                    problems = problems.Distinct().ToList()
                });
            }
        }

        return new
        {
            category = "Families/Types",
            totalChecked = familyNames.Count,
            issueCount = issues.Count,
            duplicateNames,
            issues = issues.Take(20).ToList()
        };
    }

    private static object CheckSystems(Document doc, string? pattern)
    {
        var systems = new FilteredElementCollector(doc)
            .OfClass(typeof(Autodesk.Revit.DB.MEPSystem))
            .ToList();

        var issues = new List<object>();
        foreach (var sys in systems)
        {
            var name = sys.Name;
            var problems = FindNamingIssues(name, pattern);
            if (problems.Count > 0)
            {
                issues.Add(new
                {
                    name,
                    systemType = sys.GetType().Name,
                    problems
                });
            }
        }

        return new
        {
            category = "MEP Systems",
            totalChecked = systems.Count,
            issueCount = issues.Count,
            issues = issues.Take(20).ToList()
        };
    }

    private static object CheckLevels(Document doc, string? pattern)
    {
        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .ToList();

        var issues = new List<object>();
        foreach (var level in levels)
        {
            var name = level.Name;
            var problems = FindNamingIssues(name, pattern);
            if (problems.Count > 0)
            {
                issues.Add(new
                {
                    name,
                    elevation = Math.Round(level.Elevation * 0.3048, 2),
                    problems
                });
            }
        }

        return new
        {
            category = "Levels",
            totalChecked = levels.Count,
            issueCount = issues.Count,
            issues = issues.Take(10).ToList()
        };
    }

    private static List<string> FindNamingIssues(string name, string? customPattern)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            issues.Add("Empty name");
            return issues;
        }

        if (!string.IsNullOrWhiteSpace(customPattern))
        {
            try
            {
                if (!Regex.IsMatch(name, customPattern))
                    issues.Add($"Does not match pattern: {customPattern}");
            }
            catch { }
            return issues;
        }

        if (name.Contains("  "))
            issues.Add("Contains double spaces");

        if (name != name.Trim())
            issues.Add("Has leading/trailing whitespace");

        if (Regex.IsMatch(name, @"[<>:""/\\|?*]"))
            issues.Add("Contains invalid filename characters");

        if (name.StartsWith("Copy of", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(name, @"\(\d+\)$"))
            issues.Add("Appears to be a copy/duplicate");

        if (name.Any(c => c > 127) && !name.Any(c => c >= 0x4E00 && c <= 0x9FFF))
            issues.Add("Contains non-ASCII characters");

        return issues;
    }
}
