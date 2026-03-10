using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;
using RevitChatBot.MEP.Skills.Coordination.Routing;

namespace RevitChatBot.MEP.Skills.Coordination;

[Skill("avoid_clash",
    "MEP clash avoidance rerouting. Detects clashes between two MEP categories using bounding box " +
    "overlap with tolerance, groups them into connected components, then automatically reroutes the " +
    "'shift' elements using dogleg geometry (5 segments + 4 elbows) to avoid the 'stand' elements. " +
    "Supports Pipe, Duct, CableTray, Conduit. Use mode='analyze' for dry run (no model changes).")]
[SkillParameter("shift_category", "string",
    "Category to reroute (pipe/duct/cabletray/conduit)", isRequired: true,
    allowedValues: ["pipe", "duct", "cabletray", "conduit"])]
[SkillParameter("stand_category", "string",
    "Category to avoid (pipe/duct/cabletray/conduit/structural)", isRequired: true,
    allowedValues: ["pipe", "duct", "cabletray", "conduit", "structural"])]
[SkillParameter("tolerance_mm", "number",
    "Clash detection tolerance in mm (default: 50)", isRequired: false)]
[SkillParameter("offset_mm", "number",
    "Reroute offset/clearance in mm (default: 150)", isRequired: false)]
[SkillParameter("direction", "string",
    "Preferred reroute direction (auto/up/down/left/right). 'auto' uses direction classification. Default: auto",
    isRequired: false, allowedValues: ["auto", "up", "down", "left", "right"])]
[SkillParameter("mode", "string",
    "Execution mode: 'execute' to reroute, 'analyze' for dry run (default: analyze)",
    isRequired: false, allowedValues: ["execute", "analyze"])]
[SkillParameter("level_name", "string",
    "Filter elements by level name (optional, empty = all levels)", isRequired: false)]
public class AvoidClashSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var shiftCat = (parameters.GetValueOrDefault("shift_category") as string)?.ToLowerInvariant();
        var standCat = (parameters.GetValueOrDefault("stand_category") as string)?.ToLowerInvariant();

        if (string.IsNullOrEmpty(shiftCat) || string.IsNullOrEmpty(standCat))
            return SkillResult.Fail("Both shift_category and stand_category are required.");

        if (shiftCat == standCat)
            return SkillResult.Fail("Shift and stand categories must be different.");

        double toleranceMm = ParseDouble(parameters.GetValueOrDefault("tolerance_mm"), 50);
        double offsetMm = ParseDouble(parameters.GetValueOrDefault("offset_mm"), 150);
        string directionStr = (parameters.GetValueOrDefault("direction") as string)?.ToLowerInvariant() ?? "auto";
        string mode = (parameters.GetValueOrDefault("mode") as string)?.ToLowerInvariant() ?? "analyze";
        string? levelName = parameters.GetValueOrDefault("level_name") as string;

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var shiftElements = CollectElements(document, shiftCat, levelName);
            var standElements = CollectElements(document, standCat, levelName);

            if (shiftElements.Count == 0)
                return new { error = $"No {shiftCat} elements found." };
            if (standElements.Count == 0)
                return new { error = $"No {standCat} elements found." };

            var parallelDir = RouteDirection.Down;
            var perpDir = RouteDirection.Right;
            if (directionStr != "auto")
            {
                var fixedDir = ParseDirection(directionStr);
                parallelDir = fixedDir;
                perpDir = fixedDir;
            }

            var engine = new MepRoutingEngine(
                document, toleranceMm, offsetMm, parallelDir, perpDir);

            if (mode == "execute")
            {
                using var tx = new Transaction(document, "MEP Avoid Clash Reroute");
                tx.Start();

                var rerouteResult = engine.Execute(shiftElements, standElements);

                if (rerouteResult.SuccessfulReroutes > 0)
                    tx.Commit();
                else
                    tx.RollBack();

                return FormatResult(rerouteResult, shiftCat, standCat, shiftElements.Count,
                    standElements.Count, mode);
            }
            else
            {
                var analyzeResult = engine.Analyze(shiftElements, standElements);
                return FormatResult(analyzeResult, shiftCat, standCat, shiftElements.Count,
                    standElements.Count, mode);
            }
        });

        if (result is not null)
        {
            var dict = result as IDictionary<string, object>;
            if (dict?.ContainsKey("error") == true)
                return SkillResult.Fail(dict["error"]?.ToString() ?? "Unknown error");
        }

        string message = mode == "execute"
            ? "MEP clash avoidance rerouting completed."
            : "MEP clash analysis completed (dry run - no changes made).";

        return SkillResult.Ok(message, result);
    }

    private static List<Element> CollectElements(Document doc, string category, string? levelName)
    {
        var bic = CategoryToBuiltIn(category);
        if (bic is null) return [];

        var collector = new FilteredElementCollector(doc)
            .OfCategory(bic.Value)
            .WhereElementIsNotElementType();

        var elements = collector.ToList();

        if (!string.IsNullOrEmpty(levelName))
        {
            elements = elements.Where(e =>
            {
                var lvlParam = e.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                if (lvlParam?.AsElementId() is not { } lvlId || lvlId == ElementId.InvalidElementId)
                    return false;
                var lvl = doc.GetElement(lvlId);
                return lvl?.Name?.Equals(levelName, StringComparison.OrdinalIgnoreCase) == true;
            }).ToList();
        }

        return elements;
    }

    private static BuiltInCategory? CategoryToBuiltIn(string cat) => cat switch
    {
        "pipe" => BuiltInCategory.OST_PipeCurves,
        "duct" => BuiltInCategory.OST_DuctCurves,
        "cabletray" => BuiltInCategory.OST_CableTray,
        "conduit" => BuiltInCategory.OST_Conduit,
        "structural" => BuiltInCategory.OST_StructuralFraming,
        _ => null
    };

    private static RouteDirection ParseDirection(string dir) => dir switch
    {
        "up" => RouteDirection.Up,
        "down" => RouteDirection.Down,
        "left" => RouteDirection.Left,
        "right" => RouteDirection.Right,
        _ => RouteDirection.Down
    };

    private static object FormatResult(
        RerouteResult rr, string shiftCat, string standCat,
        int shiftCount, int standCount, string mode)
    {
        return new
        {
            mode,
            shiftCategory = shiftCat,
            standCategory = standCat,
            totalShiftElements = shiftCount,
            totalStandElements = standCount,
            clashPairsFound = rr.TotalClashPairs,
            clashGroups = rr.ClashGroups,
            successfulReroutes = rr.SuccessfulReroutes,
            failedReroutes = rr.FailedReroutes,
            errors = rr.Errors.Take(20).ToList(),
            reroutedElements = rr.ReroutedElements.Take(50).Select(e => new
            {
                elementId = e.OriginalElementId,
                category = e.Category,
                direction = e.Direction,
                newSegments = e.NewSegments,
                fittings = e.Fittings
            }).ToList()
        };
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is long l) return l;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
