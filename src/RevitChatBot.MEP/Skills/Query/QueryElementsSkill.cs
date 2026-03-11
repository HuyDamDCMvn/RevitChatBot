using Autodesk.Revit.DB;
using Nice3point.Revit.Extensions;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Query;

[Skill("query_elements",
    "Query MEP elements from the Revit model by category, type name, or parameter value. " +
    "Returns a summary of matching elements with key parameters.")]
[SkillParameter("category", "string",
    "Element category: duct, pipe, equipment, fitting, electrical, fire_protection",
    isRequired: true,
    allowedValues: new[] { "duct", "pipe", "equipment", "fitting", "electrical", "fire_protection" })]
[SkillParameter("system_name", "string",
    "Optional system name to filter (e.g. 'Supply Air', 'Hot Water')",
    isRequired: false)]
[SkillParameter("max_results", "integer",
    "Maximum number of results to return (default 20)",
    isRequired: false)]
public class QueryElementsSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var category = parameters.GetValueOrDefault("category")?.ToString() ?? "duct";
        var systemName = parameters.GetValueOrDefault("system_name")?.ToString();
        var maxResults = 20;
        if (parameters.TryGetValue("max_results", out var mr) && mr is not null)
            int.TryParse(mr.ToString(), out maxResults);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var mepService = new RevitMEPService();
            var elementService = new RevitElementService();

            List<Element> elements = category switch
            {
                "duct" => mepService.GetDucts(document, systemName),
                "pipe" => mepService.GetPipes(document, systemName),
                "equipment" => mepService.GetMEPEquipment(document),
                "fitting" => elementService.GetElementsByCategory(document, BuiltInCategory.OST_DuctFitting),
                "electrical" => elementService.GetElementsByCategory(document, BuiltInCategory.OST_ElectricalEquipment),
                "fire_protection" => elementService.GetElementsByCategory(document, BuiltInCategory.OST_Sprinklers),
                _ => new List<Element>()
            };

            var summaries = elements
                .Take(maxResults)
                .Select(e => new
                {
                    Id = e.Id.Value,
                    Name = e.Name,
                    Category = e.Category?.Name ?? "Unknown",
                    Level = (e as Element)?.LevelId is ElementId lid && lid != ElementId.InvalidElementId
                        ? lid.ToElement(document)?.Name ?? "N/A"
                        : "N/A",
                    Type = e.GetTypeId().ToElement(document)?.Name ?? "N/A"
                })
                .ToList();

            return new
            {
                total = elements.Count,
                returned = summaries.Count,
                elements = summaries
            };
        });

        return SkillResult.Ok($"Found elements matching '{category}'", result);
    }
}
