using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Report;

[Skill("coordination_report",
    "Generate a coordination summary report. Aggregates clash counts by level and discipline pair, " +
    "lists linked models and their status, summarizes MEP systems health, " +
    "and provides an overall coordination score. Use before coordination meetings.")]
[SkillParameter("scope", "string",
    "'entire_model' or a specific level name. Default: 'entire_model'.",
    isRequired: false)]
[SkillParameter("category_pairs", "string",
    "Discipline pairs to check, comma-separated (e.g. 'duct-beam,pipe-beam,duct-pipe'). " +
    "Default: 'duct-beam,duct-column,pipe-beam,pipe-column,duct-pipe'.",
    isRequired: false)]
[SkillParameter("tolerance_mm", "number",
    "Clash tolerance in mm. Default: 10.",
    isRequired: false)]
public class CoordinationReportSkill : ISkill
{
    private const double MmToFeet = 1.0 / 304.8;

    private static readonly Dictionary<string, BuiltInCategory> CategoryMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["duct"] = BuiltInCategory.OST_DuctCurves,
        ["pipe"] = BuiltInCategory.OST_PipeCurves,
        ["equipment"] = BuiltInCategory.OST_MechanicalEquipment,
        ["cable_tray"] = BuiltInCategory.OST_CableTray,
        ["conduit"] = BuiltInCategory.OST_Conduit,
        ["beam"] = BuiltInCategory.OST_StructuralFraming,
        ["column"] = BuiltInCategory.OST_StructuralColumns,
        ["wall"] = BuiltInCategory.OST_Walls,
        ["floor"] = BuiltInCategory.OST_Floors,
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var scope = parameters.GetValueOrDefault("scope")?.ToString() ?? "entire_model";
        var pairsStr = parameters.GetValueOrDefault("category_pairs")?.ToString()
            ?? "duct-beam,duct-column,pipe-beam,pipe-column,duct-pipe";
        var toleranceMm = Convert.ToDouble(parameters.GetValueOrDefault("tolerance_mm") ?? 10);
        var toleranceFt = toleranceMm * MmToFeet;

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var levels = new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            Level? filterLevel = null;
            if (scope != "entire_model")
                filterLevel = levels.FirstOrDefault(l => l.Name.Contains(scope, StringComparison.OrdinalIgnoreCase));

            var pairs = pairsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim().Split('-'))
                .Where(p => p.Length == 2 && CategoryMapping.ContainsKey(p[0]) && CategoryMapping.ContainsKey(p[1]))
                .ToList();

            var clashSummary = new List<object>();
            int totalClashes = 0;

            foreach (var pair in pairs)
            {
                var catA = CategoryMapping[pair[0]];
                var catB = CategoryMapping[pair[1]];

                var elemsA = CollectElements(document, catA, filterLevel);
                var elemsB = CollectElements(document, catB, filterLevel);

                int pairClashes = 0;
                var clashByLevel = new Dictionary<string, int>();

                foreach (var a in elemsA)
                {
                    var bbA = a.get_BoundingBox(null);
                    if (bbA is null) continue;
                    var expandedMin = bbA.Min - new XYZ(toleranceFt, toleranceFt, toleranceFt);
                    var expandedMax = bbA.Max + new XYZ(toleranceFt, toleranceFt, toleranceFt);

                    foreach (var b in elemsB)
                    {
                        var bbB = b.get_BoundingBox(null);
                        if (bbB is null) continue;

                        if (expandedMin.X <= bbB.Max.X && expandedMax.X >= bbB.Min.X &&
                            expandedMin.Y <= bbB.Max.Y && expandedMax.Y >= bbB.Min.Y &&
                            expandedMin.Z <= bbB.Max.Z && expandedMax.Z >= bbB.Min.Z)
                        {
                            pairClashes++;
                            var levelName = GetElementLevel(a, levels) ?? "Unknown";
                            clashByLevel.TryGetValue(levelName, out var current);
                            clashByLevel[levelName] = current + 1;
                        }
                    }
                }

                totalClashes += pairClashes;
                clashSummary.Add(new
                {
                    pair = $"{pair[0]} vs {pair[1]}",
                    totalClashes = pairClashes,
                    byLevel = clashByLevel
                });
            }

            var links = new FilteredElementCollector(document)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Select(l => new
                {
                    name = l.Name,
                    loaded = l.GetLinkDocument() is not null,
                    pinned = l.Pinned
                })
                .ToList();

            var disconnected = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_DuctCurves)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Count(e =>
                {
                    if (e is not Autodesk.Revit.DB.Mechanical.Duct duct) return false;
                    var connectors = duct.ConnectorManager?.Connectors;
                    if (connectors is null) return false;
                    return connectors.Cast<Connector>().Any(c => !c.IsConnected);
                });

            var report = new Dictionary<string, object>
            {
                ["scope"] = scope,
                ["totalClashes"] = totalClashes,
                ["clashDetails"] = clashSummary,
                ["linkedModels"] = links,
                ["disconnectedDucts"] = disconnected,
                ["levelCount"] = levels.Count,
                ["coordinationScore"] = CalculateCoordinationScore(totalClashes, disconnected, links.Count(l => !l.loaded))
            };

            return report;
        });

        var data = (Dictionary<string, object>)result!;
        var summary = $"Coordination report: {data["totalClashes"]} total clashes, " +
                      $"{data["disconnectedDucts"]} disconnected ducts, " +
                      $"score: {data["coordinationScore"]}/100.";
        return SkillResult.Ok(summary, result);
    }

    private static List<Element> CollectElements(Document doc, BuiltInCategory cat, Level? filterLevel)
    {
        var collector = new FilteredElementCollector(doc)
            .OfCategory(cat)
            .WhereElementIsNotElementType();

        if (filterLevel is not null)
            collector = collector.WherePasses(new ElementLevelFilter(filterLevel.Id));

        return collector.ToElements().ToList();
    }

    private static string? GetElementLevel(Element elem, List<Level> levels)
    {
        var levelId = elem.LevelId;
        if (levelId != ElementId.InvalidElementId)
        {
            var level = levels.FirstOrDefault(l => l.Id == levelId);
            return level?.Name;
        }
        return null;
    }

    private static int CalculateCoordinationScore(int clashes, int disconnected, int unloadedLinks)
    {
        var score = 100;
        if (clashes > 100) score -= 30;
        else if (clashes > 50) score -= 20;
        else if (clashes > 20) score -= 10;
        else if (clashes > 0) score -= 5;

        if (disconnected > 50) score -= 20;
        else if (disconnected > 20) score -= 10;
        else if (disconnected > 0) score -= 5;

        if (unloadedLinks > 0) score -= 15;

        return Math.Max(0, score);
    }
}
