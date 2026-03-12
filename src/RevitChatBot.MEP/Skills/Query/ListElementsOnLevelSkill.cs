using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Query;

/// <summary>
/// Lists all elements on a given level, grouped by category with counts.
/// Related: query_elements
/// </summary>
[Skill("list_elements_on_level",
    "List and count all elements on a specific level, grouped by category. " +
    "Provides a high-level overview of what exists on each floor.")]
[SkillParameter("level", "string",
    "Level name (e.g. 'Level 1', 'Ground Floor'). Partial match supported.",
    isRequired: true)]
[SkillParameter("include_details", "string",
    "'true' to include sample element names per category. Default 'false'.",
    isRequired: false, allowedValues: new[] { "true", "false" })]
public class ListElementsOnLevelSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var levelName = parameters.GetValueOrDefault("level")?.ToString();
        if (string.IsNullOrWhiteSpace(levelName))
            return SkillResult.Fail("Parameter 'level' is required.");

        var includeDetails = parameters.GetValueOrDefault("include_details")?.ToString()?.ToLower() == "true";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var matchedLevel = new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name.Contains(levelName, StringComparison.OrdinalIgnoreCase));

            if (matchedLevel is null)
            {
                var availableLevels = new FilteredElementCollector(document)
                    .OfClass(typeof(Level)).Cast<Level>().Select(l => l.Name).ToList();
                return new { error = $"Level '{levelName}' not found. Available: {string.Join(", ", availableLevels)}", categories = Array.Empty<object>(), totalElements = 0, levelName = levelName };
            }

            var elements = new FluentCollector(document)
                .OnLevel(matchedLevel.Name)
                .WhereElementIsNotElementType()
                .ToList();

            var grouped = elements
                .Where(e => e.Category is not null)
                .GroupBy(e => e.Category!.Name)
                .OrderByDescending(g => g.Count())
                .Select(g => new
                {
                    category = g.Key,
                    count = g.Count(),
                    samples = includeDetails
                        ? g.Take(3).Select(e => new { id = e.Id.Value, name = e.Name }).ToList()
                        : null
                })
                .ToList();

            return new
            {
                error = (string?)null,
                levelName = matchedLevel.Name,
                totalElements = elements.Count,
                categoryCount = grouped.Count,
                categories = grouped.Cast<object>().ToArray()
            };
        });

        var data = result as dynamic;
        if (data?.error is string err && !string.IsNullOrEmpty(err))
            return SkillResult.Fail(err);

        return SkillResult.Ok(
            $"Level '{data?.levelName}': {data?.totalElements} elements in {data?.categoryCount} categories.",
            result);
    }
}
