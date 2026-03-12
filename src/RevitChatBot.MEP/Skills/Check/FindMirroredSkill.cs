using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Check;

/// <summary>
/// Detects mirrored family instances (doors, windows, equipment) that may indicate modeling errors.
/// Related: model_audit
/// </summary>
[Skill("find_mirrored_elements",
    "Find mirrored family instances (doors, windows, equipment). " +
    "Mirrored elements often indicate modeling errors — doors swinging wrong way, equipment flipped, etc.")]
[SkillParameter("category", "string",
    "Category to check: 'doors', 'windows', 'equipment', 'all'. Default 'all'.",
    isRequired: false,
    allowedValues: new[] { "doors", "windows", "equipment", "all" })]
[SkillParameter("scope", "string",
    "Scope: 'active_view' or 'entire_model'.",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
[SkillParameter("max_results", "integer",
    "Max results to return details for. Default 50.",
    isRequired: false)]
public class FindMirroredSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["doors"] = BuiltInCategory.OST_Doors,
        ["windows"] = BuiltInCategory.OST_Windows,
        ["equipment"] = BuiltInCategory.OST_MechanicalEquipment,
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var categoryStr = parameters.GetValueOrDefault("category")?.ToString() ?? "all";
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);
        var maxResults = 50;
        if (parameters.TryGetValue("max_results", out var mr) && mr is not null)
            int.TryParse(mr.ToString(), out maxResults);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var categories = categoryStr == "all"
                ? CategoryMap.Values.ToList()
                : CategoryMap.TryGetValue(categoryStr, out var bic) ? [bic] : [];

            var mirrored = new List<object>();
            int totalChecked = 0;

            foreach (var cat in categories)
            {
                var collector = ViewScopeHelper.CreateFluent(document, scope)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType();

                foreach (var elem in collector.AsEnumerable())
                {
                    totalChecked++;
                    if (elem is FamilyInstance fi && fi.Mirrored)
                    {
                        if (mirrored.Count < maxResults)
                        {
                            mirrored.Add(new
                            {
                                id = fi.Id.Value,
                                name = fi.Name,
                                category = fi.Category?.Name ?? "Unknown",
                                level = fi.LevelId is { } lid && lid != ElementId.InvalidElementId
                                    ? document.GetElement(lid)?.Name ?? "N/A" : "N/A",
                                familyName = fi.Symbol?.Family?.Name ?? "N/A"
                            });
                        }
                    }
                }
            }

            return new
            {
                totalChecked,
                mirroredCount = mirrored.Count,
                returned = Math.Min(mirrored.Count, maxResults),
                elements = mirrored
            };
        });

        var data = result as dynamic;
        var count = (int)(data?.mirroredCount ?? 0);
        return SkillResult.Ok(
            count > 0
                ? $"Found {count} mirrored elements out of {data?.totalChecked} checked."
                : $"No mirrored elements found ({data?.totalChecked} checked).",
            result);
    }
}
