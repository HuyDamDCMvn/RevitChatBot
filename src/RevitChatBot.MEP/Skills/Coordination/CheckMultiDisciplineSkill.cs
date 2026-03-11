using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Coordination;

/// <summary>
/// Checks coordination between multiple MEP disciplines within the same model.
/// Detects spatial conflicts between HVAC, plumbing, and electrical systems.
/// </summary>
[Skill("check_multi_discipline",
    "Check coordination between MEP disciplines (HVAC, plumbing, electrical) " +
    "within the same model. Detects spatial conflicts, shared space violations, " +
    "and routing interference between different systems.")]
[SkillParameter("disciplines", "string",
    "Comma-separated disciplines to check: 'hvac,plumbing,electrical'. Default: all three.",
    isRequired: false)]
[SkillParameter("tolerance_mm", "number",
    "Minimum separation distance in mm. Default: 25.",
    isRequired: false)]
[SkillParameter("level", "string",
    "Filter by level name (optional).",
    isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' to check only elements visible in the current view, " +
    "'entire_model' to check all (default: entire_model)",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class CheckMultiDisciplineSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var disciplines = parameters.GetValueOrDefault("disciplines")?.ToString() ?? "hvac,plumbing,electrical";
        var toleranceMm = ParseDouble(parameters.GetValueOrDefault("tolerance_mm"), 25);
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var toleranceFt = toleranceMm / 304.8;
        var checkSet = disciplines.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.ToLower()).ToHashSet();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var hvacElements = new List<(Element Elem, BoundingBoxXYZ BB)>();
            var plumbingElements = new List<(Element Elem, BoundingBoxXYZ BB)>();
            var electricalElements = new List<(Element Elem, BoundingBoxXYZ BB)>();

            if (checkSet.Contains("hvac"))
            {
                var hvacCats = new[] { BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_DuctFitting,
                    BuiltInCategory.OST_MechanicalEquipment };
                foreach (var cat in hvacCats)
                    hvacElements.AddRange(CollectWithBB(document, scope, cat, levelFilter));
            }

            if (checkSet.Contains("plumbing"))
            {
                var plumbCats = new[] { BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting,
                    BuiltInCategory.OST_PlumbingFixtures };
                foreach (var cat in plumbCats)
                    plumbingElements.AddRange(CollectWithBB(document, scope, cat, levelFilter));
            }

            if (checkSet.Contains("electrical"))
            {
                var elecCats = new[] { BuiltInCategory.OST_Conduit, BuiltInCategory.OST_CableTray,
                    BuiltInCategory.OST_ElectricalEquipment };
                foreach (var cat in elecCats)
                    electricalElements.AddRange(CollectWithBB(document, scope, cat, levelFilter));
            }

            var clashes = new List<object>();

            if (checkSet.Contains("hvac") && checkSet.Contains("plumbing"))
                FindClashes(hvacElements, plumbingElements, "HVAC", "Plumbing",
                    toleranceFt, document, clashes);

            if (checkSet.Contains("hvac") && checkSet.Contains("electrical"))
                FindClashes(hvacElements, electricalElements, "HVAC", "Electrical",
                    toleranceFt, document, clashes);

            if (checkSet.Contains("plumbing") && checkSet.Contains("electrical"))
                FindClashes(plumbingElements, electricalElements, "Plumbing", "Electrical",
                    toleranceFt, document, clashes);

            return new
            {
                hvacElements = hvacElements.Count,
                plumbingElements = plumbingElements.Count,
                electricalElements = electricalElements.Count,
                totalClashes = clashes.Count,
                toleranceMm,
                clashes = clashes.Take(100).ToList()
            };
        });

        return SkillResult.Ok("Multi-discipline coordination check completed.", result);
    }

    private static List<(Element Elem, BoundingBoxXYZ BB)> CollectWithBB(
        Document doc, string scope, BuiltInCategory bic, string? levelFilter)
    {
        var elements = ViewScopeHelper.CreateCollector(doc, scope)
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
            elements = elements.Where(e => GetLevelName(doc, e)
                .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        return elements
            .Select(e => (Elem: e, BB: e.get_BoundingBox(null)))
            .Where(x => x.BB is not null)
            .Select(x => (x.Elem, x.BB!))
            .ToList();
    }

    private static void FindClashes(
        List<(Element Elem, BoundingBoxXYZ BB)> setA,
        List<(Element Elem, BoundingBoxXYZ BB)> setB,
        string labelA, string labelB,
        double toleranceFt, Document doc,
        List<object> clashes)
    {
        foreach (var a in setA)
        {
            var expanded = new BoundingBoxXYZ
            {
                Min = new XYZ(a.BB.Min.X - toleranceFt, a.BB.Min.Y - toleranceFt, a.BB.Min.Z - toleranceFt),
                Max = new XYZ(a.BB.Max.X + toleranceFt, a.BB.Max.Y + toleranceFt, a.BB.Max.Z + toleranceFt)
            };

            foreach (var b in setB)
            {
                if (expanded.Min.X > b.BB.Max.X || expanded.Max.X < b.BB.Min.X ||
                    expanded.Min.Y > b.BB.Max.Y || expanded.Max.Y < b.BB.Min.Y ||
                    expanded.Min.Z > b.BB.Max.Z || expanded.Max.Z < b.BB.Min.Z)
                    continue;

                clashes.Add(new
                {
                    disciplineA = labelA,
                    elementIdA = a.Elem.Id.Value,
                    categoryA = a.Elem.Category?.Name ?? "Unknown",
                    disciplineB = labelB,
                    elementIdB = b.Elem.Id.Value,
                    categoryB = b.Elem.Category?.Name ?? "Unknown",
                    level = GetLevelName(doc, a.Elem)
                });

                if (clashes.Count >= 100) return;
            }
        }
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
