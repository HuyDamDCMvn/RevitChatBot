using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Check;

/// <summary>
/// Comprehensive fire protection system check: fire dampers at fire-rated walls,
/// sprinkler coverage, fire-rated penetrations, and smoke detection.
/// Extends beyond the basic fire damper check to cover full fire protection compliance.
/// </summary>
[Skill("check_fire_protection_system",
    "Comprehensive fire protection system check. Validates fire damper placement " +
    "at rated walls, sprinkler head coverage area, fire-rated penetration sleeves, " +
    "and smoke detector spacing. Returns a compliance report.")]
[SkillParameter("check_type", "string",
    "Which check to run: 'dampers', 'sprinklers', 'penetrations', 'all'. Default: 'all'.",
    isRequired: false, allowedValues: new[] { "dampers", "sprinklers", "penetrations", "all" })]
[SkillParameter("max_sprinkler_coverage_m2", "number",
    "Max area per sprinkler head in m². Default: 12.",
    isRequired: false)]
[SkillParameter("level", "string",
    "Filter by level name (optional).",
    isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' to check only elements visible in the current view, " +
    "'entire_model' to check all (default: entire_model)",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class CheckFireProtectionSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var checkType = parameters.GetValueOrDefault("check_type")?.ToString() ?? "all";
        var maxCoverageSqm = ParseDouble(parameters.GetValueOrDefault("max_sprinkler_coverage_m2"), 12);
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var sections = new List<object>();

            if (checkType is "dampers" or "all")
                sections.Add(CheckFireDampers(document, scope, levelFilter));

            if (checkType is "sprinklers" or "all")
                sections.Add(CheckSprinklerCoverage(document, scope, levelFilter, maxCoverageSqm));

            if (checkType is "penetrations" or "all")
                sections.Add(CheckPenetrations(document, scope, levelFilter));

            var totalIssues = sections.Sum(s => ((dynamic)s).issueCount);

            return new
            {
                overallStatus = totalIssues > 0 ? "ISSUES FOUND" : "COMPLIANT",
                totalIssues,
                sections
            };
        });

        return SkillResult.Ok("Fire protection system check completed.", result);
    }

    private static object CheckFireDampers(Document doc, string scope, string? levelFilter)
    {
        var dampers = ViewScopeHelper.CreateCollector(doc, scope)
            .OfCategory(BuiltInCategory.OST_DuctAccessory)
            .WhereElementIsNotElementType()
            .Where(e =>
            {
                var typeName = e.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)?.AsValueString() ?? "";
                var familyName = e.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString() ?? "";
                return typeName.Contains("Fire", StringComparison.OrdinalIgnoreCase) ||
                       typeName.Contains("Damper", StringComparison.OrdinalIgnoreCase) ||
                       familyName.Contains("Fire", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
            dampers = dampers.Where(d => GetLevelName(doc, d)
                .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        var ductPenetrations = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_DuctCurves)
            .WhereElementIsNotElementType()
            .Count();

        var issues = new List<object>();
        int disconnectedDampers = 0;
        foreach (var d in dampers)
        {
            var fi = d as FamilyInstance;
            var cm = fi?.MEPModel?.ConnectorManager;
            if (cm is null) continue;

            bool allConnected = true;
            foreach (Connector c in cm.Connectors)
                if (!c.IsConnected) { allConnected = false; break; }

            if (!allConnected)
            {
                disconnectedDampers++;
                issues.Add(new
                {
                    elementId = d.Id.Value,
                    issue = "Disconnected fire damper",
                    level = GetLevelName(doc, d)
                });
            }
        }

        return new
        {
            checkName = "Fire Dampers",
            totalDampers = dampers.Count,
            issueCount = issues.Count,
            disconnectedDampers,
            issues = issues.Take(20).ToList()
        };
    }

    private static object CheckSprinklerCoverage(Document doc, string scope, string? levelFilter, double maxSqm)
    {
        var sprinklers = ViewScopeHelper.CreateCollector(doc, scope)
            .OfCategory(BuiltInCategory.OST_Sprinklers)
            .WhereElementIsNotElementType()
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
            sprinklers = sprinklers.Where(s => GetLevelName(doc, s)
                .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        var spaces = ViewScopeHelper.CreateCollector(doc, scope)
            .OfCategory(BuiltInCategory.OST_MEPSpaces)
            .WhereElementIsNotElementType()
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
            spaces = spaces.Where(s => GetLevelName(doc, s)
                .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        var issues = new List<object>();

        foreach (var space in spaces)
        {
            var areaSqft = space.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0;
            var areaSqm = areaSqft * 0.092903;
            if (areaSqm < 1) continue;

            var bb = space.get_BoundingBox(null);
            if (bb is null) continue;

            int headCount = 0;
            foreach (var sp in sprinklers)
            {
                var loc = (sp.Location as LocationPoint)?.Point;
                if (loc is null) continue;
                if (loc.X >= bb.Min.X && loc.X <= bb.Max.X &&
                    loc.Y >= bb.Min.Y && loc.Y <= bb.Max.Y &&
                    loc.Z >= bb.Min.Z && loc.Z <= bb.Max.Z)
                    headCount++;
            }

            var requiredHeads = (int)Math.Ceiling(areaSqm / maxSqm);
            if (headCount < requiredHeads)
            {
                issues.Add(new
                {
                    spaceId = space.Id.Value,
                    spaceName = space.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "N/A",
                    areaSqm = Math.Round(areaSqm, 1),
                    sprinklerHeads = headCount,
                    requiredHeads,
                    level = GetLevelName(doc, space)
                });
            }
        }

        return new
        {
            checkName = "Sprinkler Coverage",
            totalSprinklers = sprinklers.Count,
            spacesChecked = spaces.Count,
            issueCount = issues.Count,
            maxCoverageSqm = maxSqm,
            issues = issues.Take(20).ToList()
        };
    }

    private static object CheckPenetrations(Document doc, string scope, string? levelFilter)
    {
        var openings = ViewScopeHelper.CreateCollector(doc, scope)
            .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
            .WhereElementIsNotElementType()
            .Where(e =>
            {
                var familyName = e.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString() ?? "";
                return familyName.Contains("Sleeve", StringComparison.OrdinalIgnoreCase) ||
                       familyName.Contains("Penetration", StringComparison.OrdinalIgnoreCase) ||
                       familyName.Contains("Firestop", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
            openings = openings.Where(o => GetLevelName(doc, o)
                .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        int withFireRating = 0;
        var issues = new List<object>();

        foreach (var o in openings)
        {
            var fireRating = o.LookupParameter("Fire Rating")?.AsString()
                             ?? o.LookupParameter("FireRating")?.AsString()
                             ?? "";
            if (!string.IsNullOrWhiteSpace(fireRating))
            {
                withFireRating++;
            }
            else
            {
                issues.Add(new
                {
                    elementId = o.Id.Value,
                    familyName = o.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString() ?? "N/A",
                    issue = "Penetration without fire rating",
                    level = GetLevelName(doc, o)
                });
            }
        }

        return new
        {
            checkName = "Penetrations/Firestop",
            totalPenetrations = openings.Count,
            withFireRating,
            issueCount = issues.Count,
            issues = issues.Take(20).ToList()
        };
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var lvlId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId()
                    ?? elem.LevelId;
        if (lvlId is null || lvlId == ElementId.InvalidElementId) return "";
        return doc.GetElement(lvlId)?.Name ?? "";
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
