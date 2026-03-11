using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("check_insulation",
    "Check missing insulation coverage on ducts and pipes. Uses InsulationLiningBase.GetInsulationIds " +
    "to identify uninsulated elements. Returns list with element id, category, system, size, level.")]
[SkillParameter("category", "string",
    "Filter: all, ducts, or pipes (default: all)", isRequired: false,
    allowedValues: new[] { "all", "ducts", "pipes" })]
[SkillParameter("systemName", "string",
    "Optional system name to filter by", isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' to check only elements visible in the current view, " +
    "'entire_model' to check all (default: entire_model)",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class CheckInsulationSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var categoryFilter = parameters.GetValueOrDefault("category")?.ToString() ?? "all";
        var systemName = parameters.GetValueOrDefault("systemName")?.ToString();
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var uninsulated = new List<object>();

            if (categoryFilter is "all" or "ducts")
            {
                var ducts = ViewScopeHelper.CreateCollector(document, scope)
                    .OfClass(typeof(Duct))
                    .WhereElementIsNotElementType()
                    .Cast<Duct>()
                    .Where(d => systemName is null ||
                        d.MEPSystem?.Name?.Contains(systemName, StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();

                foreach (var d in ducts)
                {
                    try
                    {
                        var insulationIds = InsulationLiningBase.GetInsulationIds(document, d.Id);
                        if (insulationIds is null || insulationIds.Count == 0)
                        {
                            uninsulated.Add(GetElementInfo(document, d, "Duct"));
                        }
                    }
                    catch (ArgumentException)
                    {
                        uninsulated.Add(GetElementInfo(document, d, "Duct"));
                    }
                }
            }

            if (categoryFilter is "all" or "pipes")
            {
                var pipes = ViewScopeHelper.CreateCollector(document, scope)
                    .OfClass(typeof(Pipe))
                    .WhereElementIsNotElementType()
                    .Cast<Pipe>()
                    .Where(p => systemName is null ||
                        p.MEPSystem?.Name?.Contains(systemName, StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();

                foreach (var p in pipes)
                {
                    try
                    {
                        var insulationIds = InsulationLiningBase.GetInsulationIds(document, p.Id);
                        if (insulationIds is null || insulationIds.Count == 0)
                        {
                            uninsulated.Add(GetElementInfo(document, p, "Pipe"));
                        }
                    }
                    catch (ArgumentException)
                    {
                        uninsulated.Add(GetElementInfo(document, p, "Pipe"));
                    }
                }
            }

            return new
            {
                uninsulatedCount = uninsulated.Count,
                categoryFilter,
                systemNameFilter = systemName ?? "(all)",
                uninsulated
            };
        });

        return SkillResult.Ok("Insulation check completed.", result);
    }

    private static object GetElementInfo(Document doc, Element elem, string category)
    {
        var size = elem.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";
        var systemName = (elem as MEPCurve)?.MEPSystem?.Name ?? "Unassigned";
        var levelId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId();
        var levelName = levelId is not null && levelId != ElementId.InvalidElementId
            ? doc.GetElement(levelId)?.Name ?? "N/A"
            : "N/A";

        return new
        {
            elementId = elem.Id.Value,
            category,
            system = systemName,
            size,
            level = levelName
        };
    }
}
