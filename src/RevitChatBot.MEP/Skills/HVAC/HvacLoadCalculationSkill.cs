using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using RevitChatBot.Core.Skills;
using RevitChatBot.MEP.Skills.Calculation;

namespace RevitChatBot.MEP.Skills.HVAC;

/// <summary>
/// Calculates HVAC cooling/heating load estimates per space.
/// Uses Revit Space elements and their analytical properties.
/// </summary>
[Skill("hvac_load_calculation",
    "Calculate HVAC cooling and heating load estimates for spaces/rooms in the model. " +
    "Returns area, volume, design airflow, and load data per space.")]
[SkillParameter("level_name", "string", "Filter by level name (optional)", isRequired: false)]
[SkillParameter("space_name", "string", "Filter by space name (optional)", isRequired: false)]
public class HvacLoadCalculationSkill : CalculationSkillBase
{
    protected override string SkillName => "hvac_load_calculation";

    public override async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var levelName = parameters.GetValueOrDefault("level_name")?.ToString();
        var spaceName = parameters.GetValueOrDefault("space_name")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var spaces = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_MEPSpaces)
                .WhereElementIsNotElementType()
                .Cast<Space>()
                .ToList();

            if (levelName is not null)
                spaces = spaces.Where(s =>
                    s.Level?.Name?.Contains(levelName, StringComparison.OrdinalIgnoreCase) == true).ToList();

            if (spaceName is not null)
                spaces = spaces.Where(s =>
                    s.Name?.Contains(spaceName, StringComparison.OrdinalIgnoreCase) == true).ToList();

            var spaceData = spaces.Select(s =>
            {
                var area = s.Area;
                var volume = s.Volume;

                var coolingLoad = s.get_Parameter(BuiltInParameter.ROOM_DESIGN_COOLING_LOAD_PARAM)?.AsDouble() ?? 0;
                var heatingLoad = s.get_Parameter(BuiltInParameter.ROOM_DESIGN_HEATING_LOAD_PARAM)?.AsDouble() ?? 0;
                var airflow = s.get_Parameter(BuiltInParameter.ROOM_DESIGN_SUPPLY_AIRFLOW_PARAM)?.AsDouble() ?? 0;

                return new
                {
                    name = s.Name,
                    number = s.Number,
                    level = s.Level?.Name ?? "N/A",
                    areaM2 = Math.Round(area * 0.092903, 2),
                    areaFt2 = Math.Round(area, 2),
                    volumeM3 = Math.Round(volume * 0.0283168, 2),
                    coolingLoadW = Math.Round(coolingLoad * 0.293071, 2),
                    heatingLoadW = Math.Round(heatingLoad * 0.293071, 2),
                    designAirflowCFM = Math.Round(airflow, 2),
                    designAirflowLps = Math.Round(airflow * 0.471947, 2)
                };
            }).ToList();

            var totalCooling = spaceData.Sum(s => s.coolingLoadW);
            var totalHeating = spaceData.Sum(s => s.heatingLoadW);
            var totalAirflow = spaceData.Sum(s => s.designAirflowCFM);

            return new
            {
                totalSpaces = spaceData.Count,
                totalCoolingLoadW = Math.Round(totalCooling, 2),
                totalCoolingLoadTon = Math.Round(totalCooling / 3517, 2),
                totalHeatingLoadW = Math.Round(totalHeating, 2),
                totalDesignAirflowCFM = Math.Round(totalAirflow, 2),
                spaces = spaceData
            };
        });

        var totalSpaces = (int)((dynamic)result!).totalSpaces;
        var summary = new CalcResultSummary
        {
            TotalItems = totalSpaces,
            IssueCount = 0,
            KeyMetrics =
            {
                ["totalCoolingW"] = (double)((dynamic)result!).totalCoolingLoadW,
                ["totalHeatingW"] = (double)((dynamic)result!).totalHeatingLoadW
            }
        };
        var delta = ComputeDelta(context, summary);
        SaveResultForDelta(context, summary);

        var msg = "HVAC load calculation completed.";
        if (delta is not null) msg += $"\n{delta.Summary}";

        var followUps = new List<FollowUpSuggestion>();
        if (totalSpaces > 0)
        {
            followUps.Add(new FollowUpSuggestion
            {
                SkillName = "duct_sizing_analysis",
                Reason = "Verify duct sizing matches the calculated loads"
            });
            followUps.Add(new FollowUpSuggestion
            {
                SkillName = "energy_analysis",
                Reason = "Check insulation coverage for thermal efficiency"
            });
        }

        msg = AppendFollowUps(msg, followUps);
        return OkPaginated(msg, result, totalSpaces,
            Math.Min(totalSpaces, 50), "spaces");
    }
}
