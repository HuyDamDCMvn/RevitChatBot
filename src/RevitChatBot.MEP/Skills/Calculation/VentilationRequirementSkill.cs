using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Calculation;

/// <summary>
/// Calculates required ventilation rates per ASHRAE 62.1 / TCVN 5687.
/// Compares calculated requirement against actual supply/exhaust airflow
/// in the model to identify under-ventilated spaces.
/// </summary>
[Skill("ventilation_requirement",
    "Calculate required outdoor air / ventilation rate per space using ASHRAE 62.1 or " +
    "TCVN 5687 standards. Compares against actual model airflow to find under-ventilated rooms.")]
[SkillParameter("level_name", "string", "Filter by level name (optional)", isRequired: false)]
[SkillParameter("space_name", "string", "Filter by space name (optional)", isRequired: false)]
[SkillParameter("standard", "string",
    "Ventilation standard: 'ashrae' or 'tcvn'. Default: 'ashrae'.",
    isRequired: false, allowedValues: new[] { "ashrae", "tcvn" })]
[SkillParameter("occupancy_density_m2_per_person", "number",
    "Default occupancy density (m² per person) if not set in model. Default: 10.",
    isRequired: false)]
public class VentilationRequirementSkill : CalculationSkillBase
{
    protected override string SkillName => "ventilation_requirement";

    public override async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var levelName = parameters.GetValueOrDefault("level_name")?.ToString();
        var spaceName = parameters.GetValueOrDefault("space_name")?.ToString();
        var standard = GetParamString(parameters, context, "standard", "ashrae");
        var defaultDensity = GetParamDouble(parameters, context, "occupancy_density_m2_per_person", 10.0);

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
                var areaM2 = s.Area * 0.092903;
                var volumeM3 = s.Volume * 0.0283168;
                var occupancy = (int)(s.LookupParameter("Actual Occupancy")?.AsDouble()
                    ?? s.LookupParameter("Number of People")?.AsDouble() ?? 0);
                if (occupancy <= 0 && areaM2 > 0)
                    occupancy = (int)Math.Ceiling(areaM2 / defaultDensity);

                // ASHRAE 62.1 Ventilation Rate Procedure (simplified)
                // Rp = 2.5 L/s per person, Ra = 0.3 L/s per m² (office default)
                // TCVN 5687: 25 m³/h per person = ~7 L/s per person
                double requiredLps;
                string formula;
                if (standard == "tcvn")
                {
                    requiredLps = occupancy * 7.0; // ~25 m³/h per person
                    formula = $"{occupancy} × 7.0 L/s (TCVN 5687)";
                }
                else
                {
                    var rp = 2.5;  // L/s per person
                    var ra = 0.3;  // L/s per m²
                    requiredLps = (occupancy * rp) + (areaM2 * ra);
                    formula = $"({occupancy} × {rp}) + ({areaM2:F1} × {ra}) (ASHRAE 62.1)";
                }

                var actualSupplyCfm = s.get_Parameter(BuiltInParameter.ROOM_DESIGN_SUPPLY_AIRFLOW_PARAM)?.AsDouble() ?? 0;
                var actualSupplyLps = actualSupplyCfm * 0.471947;

                var exhaustCfm = s.get_Parameter(BuiltInParameter.ROOM_DESIGN_EXHAUST_AIRFLOW_PARAM)?.AsDouble() ?? 0;
                var exhaustLps = exhaustCfm * 0.471947;

                var actualOaLps = Math.Max(actualSupplyLps, exhaustLps);
                var deficiency = requiredLps - actualOaLps;
                var status = deficiency > 0.5 ? "UNDER_VENTILATED" :
                    deficiency < -requiredLps * 0.5 ? "OVER_VENTILATED" : "OK";

                return new
                {
                    name = s.Name,
                    number = s.Number,
                    level = s.Level?.Name ?? "N/A",
                    areaM2 = Math.Round(areaM2, 2),
                    occupancy,
                    requiredLps = Math.Round(requiredLps, 1),
                    requiredCfm = Math.Round(requiredLps * 2.119, 1),
                    actualSupplyLps = Math.Round(actualOaLps, 1),
                    deficiencyLps = Math.Round(deficiency, 1),
                    formula,
                    status
                };
            }).ToList();

            var issues = spaceResults.Where(s => s.status != "OK").ToList();

            return new
            {
                standard = standard == "tcvn" ? "TCVN 5687" : "ASHRAE 62.1",
                totalSpaces = spaceResults.Count,
                underVentilated = issues.Count(i => i.status == "UNDER_VENTILATED"),
                overVentilated = issues.Count(i => i.status == "OVER_VENTILATED"),
                issues,
                allSpaces = spaceResults
            };
        });

        var totalSpaces = (int)((dynamic)result!).totalSpaces;
        var underVent = (int)((dynamic)result!).underVentilated;
        var calcSummary = new CalcResultSummary { TotalItems = totalSpaces, IssueCount = underVent };
        var delta = ComputeDelta(context, calcSummary);
        SaveResultForDelta(context, calcSummary);

        var msg = "Ventilation requirement analysis completed.";
        if (delta is not null) msg += $"\n{delta.Summary}";

        var followUps = new List<FollowUpSuggestion>();
        if (underVent > 0)
            followUps.Add(new FollowUpSuggestion
            {
                SkillName = "duct_sizing_analysis",
                Reason = $"{underVent} under-ventilated space(s) — review duct sizing"
            });

        msg = AppendFollowUps(msg, followUps);
        return OkPaginated(msg, result, totalSpaces, Math.Min(totalSpaces, 30), "spaces");
    }
}
