using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Query;

[Skill("space_analysis", "Analyze MEP spaces and optional airflow. Returns area (m²), volume (m³), and airflow compliance if checkAirflow is enabled.")]
[SkillParameter("level", "string", "Filter by level name (optional)", isRequired: false)]
[SkillParameter("checkAirflow", "boolean", "Compare design vs actual airflow (default: false)", isRequired: false)]
[SkillParameter("tolerancePct", "number", "Tolerance percentage for airflow match (default: 10)", isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' to limit to elements visible in the current view, " +
    "'entire_model' to include all (default: entire_model)",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class SpaceAnalysisSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var checkAirflow = ParseBool(parameters.GetValueOrDefault("checkAirflow"), false);
        var tolerancePct = ParseDouble(parameters.GetValueOrDefault("tolerancePct"), 10);
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var spaces = ViewScopeHelper.CreateCollector(document, scope)
                .OfCategory(BuiltInCategory.OST_MEPSpaces)
                .WhereElementIsNotElementType()
                .Cast<Space>()
                .ToList();

            if (!string.IsNullOrWhiteSpace(levelFilter))
            {
                spaces = spaces.Where(s =>
                {
                    var lvl = document.GetElement(s.LevelId) as Level;
                    return lvl?.Name?.Equals(levelFilter, StringComparison.OrdinalIgnoreCase) ?? false;
                }).ToList();
            }

            var spaceList = new List<object>();
            var airflowIssues = new List<object>();

            foreach (var space in spaces)
            {
                var areaSqFt = space.Area;
                var volumeCuFt = space.Volume;
                var areaM2 = Math.Round((areaSqFt > 0 ? areaSqFt : 0) * 0.092903, 2);
                var volumeM3 = Math.Round((volumeCuFt > 0 ? volumeCuFt : 0) * 0.0283168, 2);
                var name = space.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                var number = space.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                var levelName = (document.GetElement(space.LevelId) as Level)?.Name ?? "";

                var entry = new
                {
                    id = space.Id.Value,
                    name,
                    number,
                    level = levelName,
                    area_m2 = areaM2,
                    volume_m3 = volumeM3
                };

                if (checkAirflow)
                {
                    var designSupply = GetAirflowParamOrNull(space, "Specified Supply Airflow") ?? space.DesignSupplyAirflow;
                    var actualSupply = GetAirflowParamOrNull(space, "Actual Supply Airflow") ?? space.ActualSupplyAirflow;
                    var designReturn = GetAirflowParamOrNull(space, "Specified Return Airflow") ?? space.DesignReturnAirflow;
                    var actualReturn = GetAirflowParamOrNull(space, "Actual Return Airflow") ?? space.ActualReturnAirflow;

                    var supplyOk = WithinTolerance(designSupply, actualSupply, tolerancePct);
                    var returnOk = WithinTolerance(designReturn, actualReturn, tolerancePct);

                    if (!supplyOk || !returnOk)
                    {
                        airflowIssues.Add(new
                        {
                            id = space.Id.Value,
                            name,
                            number,
                            design_supply = Math.Round(designSupply, 2),
                            actual_supply = Math.Round(actualSupply, 2),
                            supply_ok = supplyOk,
                            design_return = Math.Round(designReturn, 2),
                            actual_return = Math.Round(actualReturn, 2),
                            return_ok = returnOk
                        });
                    }
                }

                spaceList.Add(entry);
            }

            if (checkAirflow)
                return new { spaces = spaceList, airflow_issues = airflowIssues };
            return new { spaces = spaceList };
        });

        return SkillResult.Ok("Space analysis completed.", result);
    }

    private static double? GetAirflowParamOrNull(Space space, string paramName)
    {
        var p = space.LookupParameter(paramName);
        if (p == null || !p.HasValue) return null;
        return p.AsDouble();
    }

    private static bool WithinTolerance(double design, double actual, double tolerancePct)
    {
        if (Math.Abs(design) < 0.001) return true;
        var diffPct = Math.Abs(actual - design) / Math.Abs(design) * 100;
        return diffPct <= tolerancePct;
    }

    private static bool ParseBool(object? value, bool fallback)
    {
        if (value is bool b) return b;
        if (value is string s)
            return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
        return fallback;
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is long l) return l;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
