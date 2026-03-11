using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Calculation;

/// <summary>
/// Calculates recommended pipe sizes from flow rate and velocity constraints
/// using d = sqrt(4Q / πV). Compares to actual model sizes and recommends
/// closest standard DN size.
/// </summary>
[Skill("pipe_network_sizing",
    "Calculate recommended pipe sizes from flow rate and velocity limits. " +
    "Uses d = sqrt(4Q/πV). Compares calculated vs. actual model sizes " +
    "and recommends closest standard DN.")]
[SkillParameter("system_name", "string", "Filter by system name (optional)", isRequired: false)]
[SkillParameter("max_velocity_mps", "number",
    "Max velocity in m/s. Default: 1.5 for CHW, 2.5 for general.", isRequired: false)]
[SkillParameter("pipe_type", "string",
    "Filter by pipe system type", isRequired: false,
    allowedValues: new[] { "supply", "return", "sanitary", "all" })]
public class PipeNetworkSizingSkill : CalculationSkillBase
{
    protected override string SkillName => "pipe_network_sizing";

    private static readonly double[] StandardDnMm =
    {
        15, 20, 25, 32, 40, 50, 65, 80, 100, 125,
        150, 200, 250, 300, 350, 400, 450, 500, 600
    };

    public override async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var systemName = parameters.GetValueOrDefault("system_name")?.ToString();
        var maxVel = GetParamDouble(parameters, context, "max_velocity_mps", 1.5);
        var pipeType = GetParamString(parameters, context, "pipe_type", "all");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var pipes = new FilteredElementCollector(document)
                .OfClass(typeof(Pipe))
                .WhereElementIsNotElementType()
                .Cast<Pipe>()
                .ToList();

            if (systemName is not null)
                pipes = pipes.Where(p =>
                    p.MEPSystem?.Name?.Contains(systemName, StringComparison.OrdinalIgnoreCase) == true).ToList();

            if (pipeType != "all")
            {
                pipes = pipes.Where(p =>
                {
                    var sysType = (p.MEPSystem as PipingSystem)?.SystemType;
                    return pipeType switch
                    {
                        "supply" => sysType == PipeSystemType.SupplyHydronic ||
                                    sysType == PipeSystemType.DomesticColdWater ||
                                    sysType == PipeSystemType.DomesticHotWater,
                        "return" => sysType == PipeSystemType.ReturnHydronic,
                        "sanitary" => sysType == PipeSystemType.Sanitary,
                        _ => true
                    };
                }).ToList();
            }

            var analysis = pipes.Select(p =>
            {
                var flowGpm = p.get_Parameter(BuiltInParameter.RBS_PIPE_FLOW_PARAM)?.AsDouble() ?? 0;
                var flowM3s = flowGpm * 0.0000631;
                var actualDiaFt = p.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble() ?? 0;
                var actualDiaMm = actualDiaFt * 304.8;
                var actualVelFps = p.get_Parameter(BuiltInParameter.RBS_PIPE_VELOCITY_PARAM)?.AsDouble() ?? 0;
                var actualVelMps = actualVelFps * 0.3048;

                // d = sqrt(4Q / πV)
                var calcDiaM = flowM3s > 0
                    ? Math.Sqrt(4.0 * flowM3s / (Math.PI * maxVel))
                    : 0;
                var calcDiaMm = calcDiaM * 1000;
                var recommendedDn = SnapToStandardDn(calcDiaMm);

                var actualDn = SnapToStandardDn(actualDiaMm);
                var mismatch = recommendedDn != actualDn && flowM3s > 0;

                return new
                {
                    id = p.Id.Value,
                    systemName = p.MEPSystem?.Name ?? "Unassigned",
                    flowGPM = Math.Round(flowGpm, 2),
                    flowLps = Math.Round(flowM3s * 1000, 3),
                    actualDiaMm = Math.Round(actualDiaMm, 1),
                    actualDN = $"DN{actualDn}",
                    actualVelocityMps = Math.Round(actualVelMps, 2),
                    calculatedDiaMm = Math.Round(calcDiaMm, 1),
                    recommendedDN = $"DN{recommendedDn}",
                    designVelocityMps = maxVel,
                    mismatch
                };
            }).ToList();

            var mismatches = analysis.Count(a => a.mismatch);

            return new
            {
                totalPipes = analysis.Count,
                mismatchCount = mismatches,
                designParameters = new
                {
                    maxVelocityMps = maxVel,
                    pipeType
                },
                mismatches = analysis.Where(a => a.mismatch).Take(30).ToList(),
                summary = analysis.Take(20).ToList()
            };
        });

        var totalPipes = (int)((dynamic)result!).totalPipes;
        var mismatchCount = (int)((dynamic)result!).mismatchCount;
        var calcSummary = new CalcResultSummary { TotalItems = totalPipes, IssueCount = mismatchCount };
        var delta = ComputeDelta(context, calcSummary);
        SaveResultForDelta(context, calcSummary);

        var msg = "Pipe network sizing calculation completed.";
        if (delta is not null) msg += $"\n{delta.Summary}";

        var followUps = new List<FollowUpSuggestion>();
        if (mismatchCount > 0)
            followUps.Add(new FollowUpSuggestion
            {
                SkillName = "calculate_pressure_drop",
                Reason = $"{mismatchCount} size mismatch(es) — verify pressure drop",
                PrefilledParams = { ["system_type"] = "pipe" }
            });

        msg = AppendFollowUps(msg, followUps);
        return OkPaginated(msg, result, totalPipes, Math.Min(totalPipes, 20), "pipes");
    }

    private static int SnapToStandardDn(double diameterMm)
    {
        if (diameterMm <= 0) return (int)StandardDnMm[0];
        foreach (var dn in StandardDnMm)
        {
            if (dn >= diameterMm) return (int)dn;
        }
        return (int)StandardDnMm[^1];
    }
}
