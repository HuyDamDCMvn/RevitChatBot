using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Coordination;

/// <summary>
/// Analyzes linked Revit models: extracts summary statistics,
/// structural grid/level alignment, and available categories
/// for coordination planning.
/// </summary>
[Skill("link_model_analysis",
    "Analyze linked Revit models. Reports linked model statistics: categories, " +
    "element counts, level alignment with host, shared coordinates status, " +
    "and potential coordination areas.")]
[SkillParameter("link_name", "string",
    "Name of a specific linked model to analyze. If omitted, analyzes all links.",
    isRequired: false)]
public class LinkModelAnalysisSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var linkNameFilter = parameters.GetValueOrDefault("link_name")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var links = new FilteredElementCollector(document)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            if (links.Count == 0)
                return new
                {
                    message = "No linked models found.",
                    linkedModels = 0,
                    linkAnalyses = new List<object>()
                };

            if (!string.IsNullOrWhiteSpace(linkNameFilter))
                links = links.Where(l => l.Name
                    .Contains(linkNameFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            var hostLevels = new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .ToDictionary(l => l.Name, l => l.Elevation);

            var analyses = new List<object>();

            foreach (var link in links)
            {
                var linkDoc = link.GetLinkDocument();
                if (linkDoc is null)
                {
                    analyses.Add(new
                    {
                        linkName = link.Name,
                        status = "NOT LOADED",
                        message = "Linked model is not loaded."
                    });
                    continue;
                }

                var transform = link.GetTotalTransform();

                var linkLevels = new FilteredElementCollector(linkDoc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .Select(l => new
                    {
                        name = l.Name,
                        elevationM = Math.Round(l.Elevation * 0.3048, 3),
                        matchesHost = hostLevels.Any(hl =>
                            Math.Abs(hl.Value - l.Elevation) < 0.01)
                    })
                    .ToList();

                var categoryCounts = new Dictionary<string, int>();
                var interesting = new[]
                {
                    BuiltInCategory.OST_StructuralFraming,
                    BuiltInCategory.OST_StructuralColumns,
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_Roofs,
                    BuiltInCategory.OST_Doors,
                    BuiltInCategory.OST_Windows,
                    BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_Conduit,
                    BuiltInCategory.OST_MechanicalEquipment,
                    BuiltInCategory.OST_ElectricalEquipment
                };

                foreach (var cat in interesting)
                {
                    var count = new FilteredElementCollector(linkDoc)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType()
                        .GetElementCount();
                    if (count > 0)
                        categoryCounts[cat.ToString().Replace("OST_", "")] = count;
                }

                var grids = new FilteredElementCollector(linkDoc)
                    .OfClass(typeof(Grid))
                    .GetElementCount();

                analyses.Add(new
                {
                    linkName = linkDoc.Title,
                    status = "LOADED",
                    transform = new
                    {
                        originX = Math.Round(transform.Origin.X * 0.3048, 3),
                        originY = Math.Round(transform.Origin.Y * 0.3048, 3),
                        originZ = Math.Round(transform.Origin.Z * 0.3048, 3),
                        isIdentity = transform.IsIdentity
                    },
                    levels = linkLevels,
                    levelAlignment = $"{linkLevels.Count(l => l.matchesHost)}/{linkLevels.Count} levels match host",
                    grids,
                    categoryCounts,
                    totalElements = categoryCounts.Values.Sum()
                });
            }

            return new
            {
                linkedModels = links.Count,
                hostLevelCount = hostLevels.Count,
                linkAnalyses = analyses
            };
        });

        return SkillResult.Ok("Link model analysis completed.", result);
    }
}
