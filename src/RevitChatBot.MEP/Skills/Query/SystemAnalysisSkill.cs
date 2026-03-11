using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Query;

[Skill("system_analysis", "Analyze MEP systems: ducts and pipes grouped by system name and classification, with total length and element counts.")]
[SkillParameter("typeFilter", "string", "Filter by type: all, mechanical, piping. Default: all", isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' to limit to elements visible in the current view, " +
    "'entire_model' to include all (default: entire_model)",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class SystemAnalysisSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var typeFilter = (parameters.GetValueOrDefault("typeFilter")?.ToString() ?? "all").ToLowerInvariant();
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elements = new List<(Element el, string sysName, string classification, double lengthFt)>();

            if (typeFilter is "all" or "mechanical")
            {
                var ducts = ViewScopeHelper.CreateCollector(document, scope)
                    .OfClass(typeof(Duct))
                    .WhereElementIsNotElementType()
                    .Cast<Duct>();
                foreach (var d in ducts)
                {
                    var sysName = d.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "Unassigned";
                    var classification = d.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "";
                    var len = d.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0;
                    elements.Add((d, sysName, classification, len));
                }
            }

            if (typeFilter is "all" or "piping")
            {
                var pipes = new FilteredElementCollector(document)
                    .OfClass(typeof(Pipe))
                    .WhereElementIsNotElementType()
                    .Cast<Pipe>();
                foreach (var p in pipes)
                {
                    var sysName = p.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "Unassigned";
                    var classification = p.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "";
                    var len = p.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0;
                    elements.Add((p, sysName, classification, len));
                }
            }

            var systemList = elements
                .GroupBy(x => (x.sysName, x.classification))
                .Select(g => new
                {
                    name = g.Key.sysName,
                    classification = g.Key.classification,
                    element_count = g.Count(),
                    total_length_m = Math.Round(g.Sum(x => x.lengthFt) * 0.3048, 2)
                })
                .OrderByDescending(s => s.total_length_m)
                .ToList();

            return new { systems = systemList };
        });

        return SkillResult.Ok("MEP system analysis completed.", result);
    }
}
