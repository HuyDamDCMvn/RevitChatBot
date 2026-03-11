using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Coordination;

/// <summary>
/// Advanced clash detection with filtering, grouping, and severity assessment.
/// Extends basic clash detection with level/zone filtering and clash classification.
/// </summary>
[Skill("advanced_clash_detection",
    "Advanced clash detection between MEP systems with severity classification, " +
    "level filtering, and grouped results. Provides actionable clash reports.")]
[SkillParameter("category_a", "string",
    "First MEP category", isRequired: true,
    allowedValues: new[] { "duct", "pipe", "cable_tray", "conduit", "equipment" })]
[SkillParameter("category_b", "string",
    "Second category (MEP or structural)", isRequired: true,
    allowedValues: new[] { "duct", "pipe", "cable_tray", "conduit", "equipment", "structural_column", "structural_beam", "wall" })]
[SkillParameter("level_name", "string", "Filter clashes by level (optional)", isRequired: false)]
[SkillParameter("tolerance_mm", "number", "Clash tolerance in millimeters (default: 10)", isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' to limit to elements visible in the current view, " +
    "'entire_model' to include all (default: entire_model)",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class AdvancedClashDetectionSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var catA = parameters.GetValueOrDefault("category_a")?.ToString() ?? "duct";
        var catB = parameters.GetValueOrDefault("category_b")?.ToString() ?? "pipe";
        var levelName = parameters.GetValueOrDefault("level_name")?.ToString();
        var toleranceMm = ParseDouble(parameters.GetValueOrDefault("tolerance_mm"), 10);
        var toleranceFt = toleranceMm / 304.8;
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elementsA = GetElements(document, catA, scope);
            var elementsB = GetElements(document, catB, scope);

            if (levelName is not null)
            {
                var levelId = new FilteredElementCollector(document)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault(l => l.Name.Contains(levelName, StringComparison.OrdinalIgnoreCase))?.Id;

                if (levelId is not null)
                {
                    elementsA = elementsA.Where(e => e.LevelId == levelId).ToList();
                    elementsB = elementsB.Where(e => e.LevelId == levelId).ToList();
                }
            }

            var clashes = new List<object>();
            foreach (var a in elementsA)
            {
                var bbA = a.get_BoundingBox(null);
                if (bbA is null) continue;

                var minA = new XYZ(bbA.Min.X - toleranceFt, bbA.Min.Y - toleranceFt, bbA.Min.Z - toleranceFt);
                var maxA = new XYZ(bbA.Max.X + toleranceFt, bbA.Max.Y + toleranceFt, bbA.Max.Z + toleranceFt);

                foreach (var b in elementsB)
                {
                    if (a.Id == b.Id) continue;
                    var bbB = b.get_BoundingBox(null);
                    if (bbB is null) continue;

                    if (BoxOverlap(minA, maxA, bbB.Min, bbB.Max))
                    {
                        var overlapVolume = CalculateOverlapVolume(minA, maxA, bbB.Min, bbB.Max);
                        var severity = overlapVolume > 1.0 ? "CRITICAL"
                            : overlapVolume > 0.1 ? "MAJOR"
                            : "MINOR";

                        clashes.Add(new
                        {
                            elementA = new { id = a.Id.Value, name = a.Name, category = a.Category?.Name },
                            elementB = new { id = b.Id.Value, name = b.Name, category = b.Category?.Name },
                            severity,
                            overlapVolumeFt3 = Math.Round(overlapVolume, 4)
                        });
                    }
                }
            }

            var grouped = clashes
                .GroupBy(c => ((dynamic)c).severity.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            return new
            {
                categoryA = catA,
                categoryB = catB,
                level = levelName ?? "All",
                toleranceMm,
                totalClashes = clashes.Count,
                bySeverity = grouped,
                clashes = clashes.Take(100).ToList()
            };
        });

        return SkillResult.Ok("Advanced clash detection completed.", result);
    }

    private static List<Element> GetElements(Document doc, string category, string scope)
    {
        var bic = category switch
        {
            "duct" => BuiltInCategory.OST_DuctCurves,
            "pipe" => BuiltInCategory.OST_PipeCurves,
            "cable_tray" => BuiltInCategory.OST_CableTray,
            "conduit" => BuiltInCategory.OST_Conduit,
            "equipment" => BuiltInCategory.OST_MechanicalEquipment,
            "structural_column" => BuiltInCategory.OST_StructuralColumns,
            "structural_beam" => BuiltInCategory.OST_StructuralFraming,
            "wall" => BuiltInCategory.OST_Walls,
            _ => BuiltInCategory.OST_GenericModel
        };

        return ViewScopeHelper.CreateCollector(doc, scope)
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .ToList();
    }

    private static bool BoxOverlap(XYZ minA, XYZ maxA, XYZ minB, XYZ maxB) =>
        minA.X <= maxB.X && maxA.X >= minB.X &&
        minA.Y <= maxB.Y && maxA.Y >= minB.Y &&
        minA.Z <= maxB.Z && maxA.Z >= minB.Z;

    private static double CalculateOverlapVolume(XYZ minA, XYZ maxA, XYZ minB, XYZ maxB)
    {
        var overlapX = Math.Max(0, Math.Min(maxA.X, maxB.X) - Math.Max(minA.X, minB.X));
        var overlapY = Math.Max(0, Math.Min(maxA.Y, maxB.Y) - Math.Max(minA.Y, minB.Y));
        var overlapZ = Math.Max(0, Math.Min(maxA.Z, maxB.Z) - Math.Max(minA.Z, minB.Z));
        return overlapX * overlapY * overlapZ;
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
