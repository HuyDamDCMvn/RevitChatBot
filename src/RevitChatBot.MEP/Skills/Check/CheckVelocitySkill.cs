using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("check_mep_velocity",
    "Check velocity violations for ducts and/or pipes. Reads RBS_VELOCITY parameter " +
    "and finds elements exceeding the maximum allowed velocity. " +
    "Returns count and details of violations including element IDs for highlighting.")]
[SkillParameter("category", "string",
    "Which elements to check: 'duct', 'pipe', or 'all' (default: all).",
    isRequired: false, allowedValues: new[] { "duct", "pipe", "all" })]
[SkillParameter("maxVelocity", "number",
    "Maximum allowed velocity in m/s. Default: 8.0 for ducts, 3.0 for pipes.", isRequired: false)]
[SkillParameter("system_name", "string",
    "Filter by system name (e.g. 'Supply Air', 'Chilled Water'). Optional.", isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' or 'entire_model' (default: entire_model).",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class CheckVelocitySkill : ISkill
{
    private const double DefaultDuctMaxVelocity = 8.0;
    private const double DefaultPipeMaxVelocity = 3.0;
    private const double FtPerSecToMps = 0.3048;

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var categoryFilter = parameters.GetValueOrDefault("category")?.ToString()?.ToLowerInvariant() ?? "all";
        var explicitMax = parameters.ContainsKey("maxVelocity") && parameters["maxVelocity"] is not null;
        var maxVelocity = ParseDouble(parameters.GetValueOrDefault("maxVelocity"), -1);
        var systemFilter = parameters.GetValueOrDefault("system_name")?.ToString();
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var violations = new List<object>();
            int totalDucts = 0, totalPipes = 0;

            if (categoryFilter is "all" or "duct")
            {
                var ductMaxV = explicitMax && maxVelocity > 0 ? maxVelocity : DefaultDuctMaxVelocity;
                var ducts = ViewScopeHelper.CreateCollector(document, scope)
                    .OfClass(typeof(Duct))
                    .WhereElementIsNotElementType()
                    .Cast<Duct>()
                    .ToList();

                if (!string.IsNullOrWhiteSpace(systemFilter))
                    ducts = ducts.Where(d => d.MEPSystem?.Name?.Contains(systemFilter, StringComparison.OrdinalIgnoreCase) == true).ToList();

                totalDucts = ducts.Count;
                foreach (var d in ducts)
                {
                    var velocityMps = (d.get_Parameter(BuiltInParameter.RBS_VELOCITY)?.AsDouble() ?? 0) * FtPerSecToMps;
                    if (velocityMps <= ductMaxV) continue;

                    violations.Add(new
                    {
                        elementId = d.Id.Value,
                        elementCategory = "Duct",
                        size = d.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A",
                        actualVelocityMps = Math.Round(velocityMps, 2),
                        maxAllowedMps = ductMaxV,
                        systemName = d.MEPSystem?.Name ?? "Unassigned",
                        level = GetLevelName(document, d)
                    });
                }
            }

            if (categoryFilter is "all" or "pipe")
            {
                var pipeMaxV = explicitMax && maxVelocity > 0 ? maxVelocity : DefaultPipeMaxVelocity;
                var pipes = ViewScopeHelper.CreateCollector(document, scope)
                    .OfClass(typeof(Pipe))
                    .WhereElementIsNotElementType()
                    .Cast<Pipe>()
                    .ToList();

                if (!string.IsNullOrWhiteSpace(systemFilter))
                    pipes = pipes.Where(p => p.MEPSystem?.Name?.Contains(systemFilter, StringComparison.OrdinalIgnoreCase) == true).ToList();

                totalPipes = pipes.Count;
                foreach (var p in pipes)
                {
                    var velocityMps = (p.get_Parameter(BuiltInParameter.RBS_VELOCITY)?.AsDouble() ?? 0) * FtPerSecToMps;
                    if (velocityMps <= pipeMaxV) continue;

                    violations.Add(new
                    {
                        elementId = p.Id.Value,
                        elementCategory = "Pipe",
                        size = p.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A",
                        actualVelocityMps = Math.Round(velocityMps, 2),
                        maxAllowedMps = pipeMaxV,
                        systemName = p.MEPSystem?.Name ?? "Unassigned",
                        level = GetLevelName(document, p)
                    });
                }
            }

            return new
            {
                totalDucts,
                totalPipes,
                totalChecked = totalDucts + totalPipes,
                violationCount = violations.Count,
                violations = violations.Take(100).ToList()
            };
        });

        return SkillResult.Ok("MEP velocity check completed.", result);
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var levelId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId();
        if (levelId is null || levelId == ElementId.InvalidElementId) return "N/A";
        return doc.GetElement(levelId)?.Name ?? "N/A";
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
