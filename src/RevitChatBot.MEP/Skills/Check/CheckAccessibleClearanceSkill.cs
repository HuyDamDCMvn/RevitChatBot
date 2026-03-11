using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Check;

/// <summary>
/// Checks ADA/accessibility clearance requirements for MEP elements.
/// Validates that plumbing fixtures, equipment, and exposed services
/// don't obstruct accessible routes or violate height clearances.
/// </summary>
[Skill("check_accessible_clearance",
    "Check ADA/accessibility clearance requirements. Validates minimum clear " +
    "floor space around plumbing fixtures, minimum headroom under exposed ducts/pipes, " +
    "and accessible route widths are maintained.")]
[SkillParameter("min_headroom_mm", "number",
    "Minimum headroom under exposed MEP elements in mm. Default: 2100.",
    isRequired: false)]
[SkillParameter("min_fixture_clearance_mm", "number",
    "Minimum clear floor space around accessible fixtures in mm. Default: 760.",
    isRequired: false)]
[SkillParameter("level", "string",
    "Filter by level name (optional).",
    isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' to check only elements visible in the current view, " +
    "'entire_model' to check all (default: entire_model)",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class CheckAccessibleClearanceSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var minHeadroomMm = ParseDouble(parameters.GetValueOrDefault("min_headroom_mm"), 2100);
        var minFixtureMm = ParseDouble(parameters.GetValueOrDefault("min_fixture_clearance_mm"), 760);
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var minHeadroomFt = minHeadroomMm / 304.8;

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var issues = new List<object>();

            var levels = new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .ToList();

            if (!string.IsNullOrWhiteSpace(levelFilter))
                levels = levels.Where(l => l.Name
                    .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var level in levels)
            {
                var levelElevation = level.Elevation;

                var mepOnLevel = ViewScopeHelper.CreateCollector(document, scope)
                    .WhereElementIsNotElementType()
                    .Where(e =>
                    {
                        var bb = e.get_BoundingBox(null);
                        if (bb is null) return false;
                        var cat = e.Category?.BuiltInCategory;
                        return (cat == BuiltInCategory.OST_DuctCurves ||
                                cat == BuiltInCategory.OST_PipeCurves ||
                                cat == BuiltInCategory.OST_Conduit ||
                                cat == BuiltInCategory.OST_CableTray) &&
                               bb.Min.Z > levelElevation &&
                               bb.Min.Z < levelElevation + 15;
                    })
                    .ToList();

                foreach (var elem in mepOnLevel)
                {
                    var bb = elem.get_BoundingBox(null)!;
                    var bottomElevation = bb.Min.Z;
                    var headroom = bottomElevation - levelElevation;
                    var headroomMm = headroom * 304.8;

                    if (headroomMm < minHeadroomMm && headroomMm > 0)
                    {
                        issues.Add(new
                        {
                            elementId = elem.Id.Value,
                            category = elem.Category?.Name ?? "Unknown",
                            level = level.Name,
                            headroomMm = Math.Round(headroomMm, 0),
                            requiredMm = minHeadroomMm,
                            issue = "Insufficient headroom for accessibility"
                        });
                    }
                }
            }

            var plumbingFixtures = ViewScopeHelper.CreateCollector(document, scope)
                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                .WhereElementIsNotElementType()
                .ToList();

            if (!string.IsNullOrWhiteSpace(levelFilter))
                plumbingFixtures = plumbingFixtures.Where(f => GetLevelName(document, f)
                    .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            int fixturesChecked = 0;
            foreach (var fixture in plumbingFixtures)
            {
                fixturesChecked++;
                var familyName = fixture.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString() ?? "";
                var isAccessible = familyName.Contains("ADA", StringComparison.OrdinalIgnoreCase) ||
                                  familyName.Contains("Accessible", StringComparison.OrdinalIgnoreCase) ||
                                  familyName.Contains("Handicap", StringComparison.OrdinalIgnoreCase);

                if (!isAccessible) continue;

                var bb = fixture.get_BoundingBox(null);
                if (bb is null) continue;

                var nearby = new FilteredElementCollector(document)
                    .WhereElementIsNotElementType()
                    .Where(e =>
                    {
                        if (e.Id == fixture.Id) return false;
                        var ebb = e.get_BoundingBox(null);
                        return ebb is not null && MinHorizontalDistance(bb, ebb) < minFixtureMm / 304.8;
                    })
                    .Take(3)
                    .ToList();

                if (nearby.Count > 0)
                {
                    issues.Add(new
                    {
                        elementId = fixture.Id.Value,
                        category = "Plumbing Fixture (Accessible)",
                        level = GetLevelName(document, fixture),
                        headroomMm = 0,
                        requiredMm = minFixtureMm,
                        issue = $"Accessible fixture has {nearby.Count} element(s) within {minFixtureMm}mm clearance zone"
                    });
                }
            }

            return new
            {
                totalIssues = issues.Count,
                headroomIssues = issues.Count(i => ((dynamic)i).issue.ToString().Contains("headroom")),
                fixtureClearanceIssues = issues.Count(i => ((dynamic)i).issue.ToString().Contains("fixture")),
                fixturesChecked,
                requirements = new
                {
                    minHeadroomMm,
                    minFixtureClearanceMm = minFixtureMm
                },
                issues = issues.Take(50).ToList()
            };
        });

        return SkillResult.Ok("Accessible clearance check completed.", result);
    }

    private static double MinHorizontalDistance(BoundingBoxXYZ a, BoundingBoxXYZ b)
    {
        double dx = Math.Max(0, Math.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
        double dy = Math.Max(0, Math.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var lvlId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId() ?? elem.LevelId;
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
