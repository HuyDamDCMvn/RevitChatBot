using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Calculation;

/// <summary>
/// Calculates recommended duct sizes from first principles using A = Q / V,
/// then compares to current Revit model sizes. Uses Equal Friction method
/// and standard duct dimensions.
/// </summary>
[Skill("duct_network_sizing",
    "Calculate recommended duct sizes from airflow and velocity constraints. " +
    "Uses A = Q/V with equal friction method. Compares calculated vs. actual " +
    "model sizes and recommends closest standard dimensions.")]
[SkillParameter("system_name", "string", "Filter by system name (optional)", isRequired: false)]
[SkillParameter("max_velocity_mps", "number",
    "Max velocity for main ducts in m/s. Default: 8.", isRequired: false)]
[SkillParameter("branch_velocity_mps", "number",
    "Max velocity for branch ducts in m/s. Default: 5.", isRequired: false)]
[SkillParameter("aspect_ratio", "number",
    "Max aspect ratio W:H for rectangular ducts. Default: 3.", isRequired: false)]
public class DuctNetworkSizingSkill : CalculationSkillBase
{
    protected override string SkillName => "duct_network_sizing";

    private static readonly int[] StandardSizesMm =
    {
        100, 125, 150, 200, 250, 300, 350, 400, 450, 500,
        550, 600, 650, 700, 750, 800, 900, 1000, 1100, 1200,
        1400, 1500, 1600, 1800, 2000
    };

    public override async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var systemName = parameters.GetValueOrDefault("system_name")?.ToString();
        var maxVelMain = GetParamDouble(parameters, context, "max_velocity_mps", 8.0);
        var maxVelBranch = GetParamDouble(parameters, context, "branch_velocity_mps", 5.0);
        var maxAR = GetParamDouble(parameters, context, "aspect_ratio", 3.0);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var ducts = new FilteredElementCollector(document)
                .OfClass(typeof(Duct))
                .WhereElementIsNotElementType()
                .Cast<Duct>()
                .ToList();

            if (systemName is not null)
                ducts = ducts.Where(d =>
                    d.MEPSystem?.Name?.Contains(systemName, StringComparison.OrdinalIgnoreCase) == true).ToList();

            var analysis = ducts.Select(d =>
            {
                var flowCfm = d.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM)?.AsDouble() ?? 0;
                var flowM3s = flowCfm * 0.000471947;
                var actualSizeStr = d.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";
                var actualVelFpm = d.get_Parameter(BuiltInParameter.RBS_VELOCITY)?.AsDouble() ?? 0;
                var actualVelMps = actualVelFpm * 0.00508;

                var isBranch = flowCfm < 800;
                var designVel = isBranch ? maxVelBranch : maxVelMain;

                // A_required = Q / V
                var aRequired = flowM3s > 0 ? flowM3s / designVel : 0;
                var aRequiredMm2 = aRequired * 1_000_000;

                // Calculate recommended rectangular size
                var wCalc = Math.Sqrt(aRequiredMm2 * maxAR);
                var hCalc = aRequiredMm2 > 0 ? aRequiredMm2 / wCalc : 0;

                var wStd = SnapToStandard(wCalc);
                var hStd = SnapToStandard(hCalc);
                if (wStd < hStd) (wStd, hStd) = (hStd, wStd);

                // Equivalent diameter
                var deCalc = aRequired > 0
                    ? 1.3 * Math.Pow(wStd * hStd, 0.625) / Math.Pow(wStd + hStd, 0.25)
                    : 0;

                var recommendedSize = $"{wStd}×{hStd}";
                var mismatch = recommendedSize != actualSizeStr;

                return new
                {
                    id = d.Id.Value,
                    systemName = d.MEPSystem?.Name ?? "Unassigned",
                    flowCFM = Math.Round(flowCfm, 1),
                    flowLps = Math.Round(flowM3s * 1000, 1),
                    actualSize = actualSizeStr,
                    actualVelocityMps = Math.Round(actualVelMps, 2),
                    recommendedSize,
                    recommendedDeMm = Math.Round(deCalc, 0),
                    requiredAreaMm2 = Math.Round(aRequiredMm2, 0),
                    designVelocityMps = designVel,
                    classification = isBranch ? "branch" : "main",
                    mismatch
                };
            }).ToList();

            var mismatches = analysis.Count(a => a.mismatch);

            return new
            {
                totalDucts = analysis.Count,
                mismatchCount = mismatches,
                designParameters = new
                {
                    mainVelocityMps = maxVelMain,
                    branchVelocityMps = maxVelBranch,
                    maxAspectRatio = maxAR
                },
                mismatches = analysis.Where(a => a.mismatch).Take(30).ToList(),
                summary = analysis.Take(20).ToList()
            };
        });

        var totalDucts = (int)((dynamic)result!).totalDucts;
        var mismatchCount = (int)((dynamic)result!).mismatchCount;
        var calcSummary = new CalcResultSummary { TotalItems = totalDucts, IssueCount = mismatchCount };
        var delta = ComputeDelta(context, calcSummary);
        SaveResultForDelta(context, calcSummary);

        var msg = "Duct network sizing calculation completed.";
        if (delta is not null) msg += $"\n{delta.Summary}";

        var followUps = new List<FollowUpSuggestion>();
        if (mismatchCount > 0)
            followUps.Add(new FollowUpSuggestion
            {
                SkillName = "calculate_pressure_drop",
                Reason = $"{mismatchCount} size mismatch(es) — verify pressure drop impact",
                PrefilledParams = { ["system_type"] = "duct" }
            });

        msg = AppendFollowUps(msg, followUps);
        return OkPaginated(msg, result, totalDucts, Math.Min(totalDucts, 20), "ducts");
    }

    private static int SnapToStandard(double valueMm)
    {
        if (valueMm <= 0) return StandardSizesMm[0];
        foreach (var s in StandardSizesMm)
        {
            if (s >= valueMm) return s;
        }
        return StandardSizesMm[^1];
    }
}
