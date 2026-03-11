using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("check_duct_velocity",
    "Check duct velocity violations. Collects all ducts, reads velocity (RBS_VELOCITY), and finds " +
    "violations exceeding the maximum allowed velocity. Returns count and details of violations.")]
[SkillParameter("maxVelocity", "number",
    "Maximum allowed velocity in m/s (default: 8.0)", isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' to check only elements visible in the current view, " +
    "'entire_model' to check all (default: entire_model)",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class CheckVelocitySkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var maxVelocity = ParseDouble(parameters.GetValueOrDefault("maxVelocity"), 8.0);
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var ducts = ViewScopeHelper.CreateCollector(document, scope)
                .OfClass(typeof(Duct))
                .WhereElementIsNotElementType()
                .Cast<Duct>()
                .ToList();

            var violations = new List<object>();
            foreach (var d in ducts)
            {
                var velocityParam = d.get_Parameter(BuiltInParameter.RBS_VELOCITY);
                var velocityFtPerSec = velocityParam?.AsDouble() ?? 0;
                var velocityMps = velocityFtPerSec * 0.3048;

                if (velocityMps <= maxVelocity) continue;

                var size = d.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";
                var systemName = d.MEPSystem?.Name ?? "Unassigned";
                var levelId = d.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId();
                var levelName = levelId is not null && levelId != ElementId.InvalidElementId
                    ? document.GetElement(levelId)?.Name ?? "N/A"
                    : "N/A";

                violations.Add(new
                {
                    elementId = d.Id.Value,
                    size,
                    actualVelocityMps = Math.Round(velocityMps, 2),
                    systemName,
                    level = levelName
                });
            }

            return new
            {
                totalDucts = ducts.Count,
                violationCount = violations.Count,
                maxVelocityMps = maxVelocity,
                violations
            };
        });

        return SkillResult.Ok("Duct velocity check completed.", result);
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
