using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Query;

/// <summary>
/// Finds openings (shaft, floor, wall) in the model for MEP penetration analysis.
/// Related: check_penetration
/// </summary>
[Skill("find_openings",
    "Find shaft openings, floor openings, and wall openings in the model. " +
    "Useful for MEP penetration analysis and coordination with structural/architectural models.")]
[SkillParameter("opening_type", "string",
    "Type of opening: 'shaft', 'floor', 'wall', 'all'. Default 'all'.",
    isRequired: false,
    allowedValues: new[] { "shaft", "floor", "wall", "all" })]
[SkillParameter("level", "string",
    "Filter by level name.",
    isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' or 'entire_model'.",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
[SkillParameter("max_results", "integer",
    "Max results to return. Default 50.",
    isRequired: false)]
public class FindOpeningsSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory> OpeningCategories = new()
    {
        ["shaft"] = BuiltInCategory.OST_ShaftOpening,
        ["floor"] = BuiltInCategory.OST_FloorOpening,
        ["wall"] = BuiltInCategory.OST_SWallRectOpening,
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var openingType = parameters.GetValueOrDefault("opening_type")?.ToString() ?? "all";
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);
        var maxResults = 50;
        if (parameters.TryGetValue("max_results", out var mr) && mr is not null)
            int.TryParse(mr.ToString(), out maxResults);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var categories = openingType == "all"
                ? OpeningCategories.Values.ToList()
                : OpeningCategories.TryGetValue(openingType, out var cat) ? new List<BuiltInCategory> { cat } : [];

            var allOpenings = new List<object>();
            var countByType = new Dictionary<string, int>();

            foreach (var bic in categories)
            {
                var collector = ViewScopeHelper.CreateFluent(document, scope)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType();

                if (!string.IsNullOrWhiteSpace(levelFilter))
                    collector.OnLevel(levelFilter);

                var elements = collector.ToList();
                var typeName = bic switch
                {
                    BuiltInCategory.OST_ShaftOpening => "Shaft",
                    BuiltInCategory.OST_FloorOpening => "Floor",
                    BuiltInCategory.OST_SWallRectOpening => "Wall",
                    _ => "Other"
                };

                countByType[typeName] = elements.Count;

                foreach (var e in elements.Take(maxResults - allOpenings.Count))
                {
                    var bb = e.get_BoundingBox(null);
                    allOpenings.Add(new
                    {
                        id = e.Id.Value,
                        name = e.Name,
                        type = typeName,
                        level = e.LevelId is { } lid && lid != ElementId.InvalidElementId
                            ? document.GetElement(lid)?.Name ?? "N/A" : "N/A",
                        width_mm = bb != null ? Math.Round((bb.Max.X - bb.Min.X) * 304.8, 0) : 0,
                        height_mm = bb != null ? Math.Round((bb.Max.Z - bb.Min.Z) * 304.8, 0) : 0
                    });

                    if (allOpenings.Count >= maxResults) break;
                }
            }

            var total = countByType.Values.Sum();
            return new
            {
                totalOpenings = total,
                returned = allOpenings.Count,
                countByType,
                openings = allOpenings
            };
        });

        var data = result as dynamic;
        var countByTypeDict = data?.countByType as Dictionary<string, int> ?? new Dictionary<string, int>();
        var countByTypeStr = string.Join(", ", countByTypeDict.Select(kv => $"{kv.Key}: {kv.Value}"));
        return SkillResult.Ok(
            $"Found {data?.totalOpenings} openings ({countByTypeStr}).",
            result);
    }
}
