using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Calculation;

/// <summary>
/// Calculates required water flow and airflow from cooling/heating loads.
/// Uses Q = m × Cp × ΔT formulas for both water and air.
/// </summary>
[Skill("flow_from_load",
    "Calculate required chilled water flow (L/s) and supply airflow (m³/s) " +
    "from cooling/heating loads. Reads Revit Space loads and applies " +
    "Q = m × Cp × ΔT. Supports Vietnamese standards (TCVN) and ASHRAE.")]
[SkillParameter("level_name", "string", "Filter by level name (optional)", isRequired: false)]
[SkillParameter("space_name", "string", "Filter by space name (optional)", isRequired: false)]
[SkillParameter("chw_delta_t", "number",
    "Chilled water ΔT in °C (default: 5 for 7→12°C)", isRequired: false)]
[SkillParameter("air_delta_t", "number",
    "Supply air ΔT in °C (default: 10 = room 24°C minus supply 14°C)", isRequired: false)]
public class FlowFromLoadSkill : CalculationSkillBase
{
    protected override string SkillName => "flow_from_load";

    private const double CpWater = 4.186;   // kJ/(kg·°C)
    private const double CpAir = 1.005;     // kJ/(kg·°C)
    private const double RhoAir = 1.2;      // kg/m³

    public override async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var levelName = parameters.GetValueOrDefault("level_name")?.ToString();
        var spaceName = parameters.GetValueOrDefault("space_name")?.ToString();
        var chwDeltaT = GetParamDouble(parameters, context, "chw_delta_t", 5.0);
        var airDeltaT = GetParamDouble(parameters, context, "air_delta_t", 10.0);

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

            var spaceResults = spaces.Select(s =>
            {
                var coolingBtu = s.get_Parameter(BuiltInParameter.ROOM_DESIGN_COOLING_LOAD_PARAM)?.AsDouble() ?? 0;
                var coolingKw = coolingBtu * 0.293071 / 1000.0;

                // Q_water = Q_cool / (Cp × ΔT)
                var waterFlowLps = coolingKw > 0 ? coolingKw / (CpWater * chwDeltaT) : 0;
                var waterFlowGpm = waterFlowLps * 15.85;

                // Q_air = Q_cool / (ρ × Cp × ΔT)
                var airFlowM3s = coolingKw > 0 ? coolingKw / (RhoAir * CpAir * airDeltaT) : 0;
                var airFlowLps = airFlowM3s * 1000;
                var airFlowCfm = airFlowM3s * 2119;

                return new
                {
                    name = s.Name,
                    number = s.Number,
                    level = s.Level?.Name ?? "N/A",
                    areaM2 = Math.Round(s.Area * 0.092903, 2),
                    coolingKW = Math.Round(coolingKw, 3),
                    coolingTon = Math.Round(coolingKw / 3.517, 3),
                    waterFlowLps = Math.Round(waterFlowLps, 4),
                    waterFlowGPM = Math.Round(waterFlowGpm, 2),
                    airFlowLps = Math.Round(airFlowLps, 1),
                    airFlowCFM = Math.Round(airFlowCfm, 1),
                    airFlowM3h = Math.Round(airFlowM3s * 3600, 1)
                };
            }).ToList();

            var totalCooling = spaceResults.Sum(s => s.coolingKW);
            var totalWater = spaceResults.Sum(s => s.waterFlowLps);
            var totalAir = spaceResults.Sum(s => s.airFlowLps);

            return new
            {
                totalSpaces = spaceResults.Count,
                parameters = new { chwDeltaT, airDeltaT },
                totals = new
                {
                    coolingKW = Math.Round(totalCooling, 2),
                    coolingTon = Math.Round(totalCooling / 3.517, 2),
                    waterFlowLps = Math.Round(totalWater, 2),
                    waterFlowGPM = Math.Round(totalWater * 15.85, 2),
                    airFlowLps = Math.Round(totalAir, 1),
                    airFlowCFM = Math.Round(totalAir * 2.119, 1)
                },
                spaces = spaceResults
            };
        });

        var totalSpaces = (int)((dynamic)result!).totalSpaces;
        var summary = new CalcResultSummary
        {
            TotalItems = totalSpaces,
            IssueCount = 0,
            KeyMetrics =
            {
                ["totalCoolingKW"] = (double)((dynamic)result!).totals.coolingKW,
                ["totalWaterLps"] = (double)((dynamic)result!).totals.waterFlowLps,
                ["totalAirLps"] = (double)((dynamic)result!).totals.airFlowLps
            }
        };
        var delta = ComputeDelta(context, summary);
        SaveResultForDelta(context, summary);

        var msg = "Flow-from-load calculation completed.";
        if (delta is not null) msg += $"\n{delta.Summary}";

        var followUps = new List<FollowUpSuggestion>
        {
            new() { SkillName = "pipe_sizing_analysis", Reason = "Verify pipe sizes match calculated water flows" },
            new() { SkillName = "duct_sizing_analysis", Reason = "Verify duct sizes match calculated airflows" }
        };
        msg = AppendFollowUps(msg, followUps);
        return OkPaginated(msg, result, totalSpaces, Math.Min(totalSpaces, 40), "spaces");
    }
}
