using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Check;

/// <summary>
/// Checks that mechanical equipment has adequate access/maintenance clearance.
/// Validates minimum distances from walls, other equipment, and structure.
/// </summary>
[Skill("check_equipment_access",
    "Check that mechanical equipment has adequate maintenance access clearance. " +
    "Validates minimum distances from walls, other equipment, and structural elements. " +
    "Flags equipment that violates clearance requirements.")]
[SkillParameter("min_front_clearance_mm", "number",
    "Minimum front clearance in mm for maintenance access. Default: 900.",
    isRequired: false)]
[SkillParameter("min_side_clearance_mm", "number",
    "Minimum side clearance in mm. Default: 600.",
    isRequired: false)]
[SkillParameter("level", "string",
    "Filter by level name (optional).",
    isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' to check only elements visible in the current view, " +
    "'entire_model' to check all (default: entire_model)",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class CheckEquipmentAccessSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var minFrontMm = ParseDouble(parameters.GetValueOrDefault("min_front_clearance_mm"), 900);
        var minSideMm = ParseDouble(parameters.GetValueOrDefault("min_side_clearance_mm"), 600);
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var minFrontFt = minFrontMm / 304.8;
        var minSideFt = minSideMm / 304.8;

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var equipment = ViewScopeHelper.CreateCollector(document, scope)
                .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                .WhereElementIsNotElementType()
                .ToList();

            if (!string.IsNullOrWhiteSpace(levelFilter))
                equipment = equipment.Where(e => GetLevelName(document, e)
                    .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            var allBbs = equipment.Select(e => new
            {
                Element = e,
                BB = e.get_BoundingBox(null)
            }).Where(x => x.BB is not null).ToList();

            var walls = new FilteredElementCollector(document)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .Select(w => w.get_BoundingBox(null))
                .Where(bb => bb is not null)
                .ToList();

            var violations = new List<object>();

            foreach (var eq in allBbs)
            {
                var bb = eq.BB!;
                var expandedFront = new BoundingBoxXYZ
                {
                    Min = new XYZ(bb.Min.X - minFrontFt, bb.Min.Y - minFrontFt, bb.Min.Z),
                    Max = new XYZ(bb.Max.X + minFrontFt, bb.Max.Y + minFrontFt, bb.Max.Z)
                };

                bool tooCloseToWall = false;
                double minWallDistMm = double.MaxValue;

                foreach (var wallBb in walls)
                {
                    var dist = MinDistance(bb, wallBb!);
                    var distMm = dist * 304.8;
                    if (distMm < minWallDistMm) minWallDistMm = distMm;
                    if (distMm < minSideMm) tooCloseToWall = true;
                }

                bool tooCloseToEquipment = false;
                double minEqDistMm = double.MaxValue;

                foreach (var other in allBbs)
                {
                    if (other.Element.Id == eq.Element.Id) continue;
                    var dist = MinDistance(bb, other.BB!);
                    var distMm = dist * 304.8;
                    if (distMm < minEqDistMm) minEqDistMm = distMm;
                    if (distMm < minSideMm) tooCloseToEquipment = true;
                }

                if (tooCloseToWall || tooCloseToEquipment)
                {
                    var familyName = eq.Element.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString() ?? "N/A";
                    violations.Add(new
                    {
                        elementId = eq.Element.Id.Value,
                        family = familyName,
                        level = GetLevelName(document, eq.Element),
                        minWallDistanceMm = Math.Round(Math.Min(minWallDistMm, 99999), 0),
                        minEquipmentDistanceMm = Math.Round(Math.Min(minEqDistMm, 99999), 0),
                        issues = new List<string>()
                            .Concat(tooCloseToWall ? [$"Wall clearance < {minSideMm}mm"] : [])
                            .Concat(tooCloseToEquipment ? [$"Equipment clearance < {minSideMm}mm"] : [])
                            .ToList()
                    });
                }
            }

            return new
            {
                totalEquipment = equipment.Count,
                violationCount = violations.Count,
                requirements = new { frontClearanceMm = minFrontMm, sideClearanceMm = minSideMm },
                violations = violations.Take(50).ToList()
            };
        });

        return SkillResult.Ok("Equipment access clearance check completed.", result);
    }

    private static double MinDistance(BoundingBoxXYZ a, BoundingBoxXYZ b)
    {
        double dx = Math.Max(0, Math.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
        double dy = Math.Max(0, Math.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
        double dz = Math.Max(0, Math.Max(a.Min.Z - b.Max.Z, b.Min.Z - a.Max.Z));
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
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
