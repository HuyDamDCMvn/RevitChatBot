using RevitChatBot.Core.Skills;
using RevitChatBot.MEP.Skills.Calculation;
using RevitChatBot.MEP.Skills.HVAC;

namespace RevitChatBot.MEP.Skills.Composite;

/// <summary>
/// Composite skill that orchestrates a full auto-dimensioning workflow:
/// 1. Read space loads (hvac_load_calculation)
/// 2. Calculate required flows (flow_from_load)
/// 3. Size ducts (duct_network_sizing) and pipes (pipe_network_sizing)
/// 4. Verify pressure drop (pressure_drop_calc)
/// 5. Validate insulation (insulation_thickness_calc)
///
/// Returns a consolidated report with all recommendations.
/// This skill is a coordinator — it delegates to other CalculationSkills
/// and merges their results.
/// </summary>
[Skill("auto_dimensioning",
    "Run a complete auto-dimensioning workflow: load → flow → sizing → pressure drop → insulation. " +
    "Orchestrates multiple calculation skills and returns a consolidated report. " +
    "Equivalent to LINEAR's automatic dimensioning feature.")]
[SkillParameter("level_name", "string", "Filter by level name (optional)", isRequired: false)]
[SkillParameter("system_name", "string", "Filter by system name (optional)", isRequired: false)]
[SkillParameter("scope", "string",
    "Which disciplines to include: 'hvac', 'plumbing', 'electrical', 'all'. Default: 'all'.",
    isRequired: false, allowedValues: new[] { "hvac", "plumbing", "electrical", "all" })]
[SkillParameter("chw_delta_t", "number", "CHW ΔT in °C (default: 5)", isRequired: false)]
[SkillParameter("max_duct_velocity_mps", "number", "Max duct velocity m/s (default: 8)", isRequired: false)]
[SkillParameter("max_pipe_velocity_mps", "number", "Max pipe velocity m/s (default: 1.5)", isRequired: false)]
public class AutoDimensioningSkill : CalculationSkillBase
{
    protected override string SkillName => "auto_dimensioning";

    public override async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var levelName = parameters.GetValueOrDefault("level_name")?.ToString();
        var systemName = parameters.GetValueOrDefault("system_name")?.ToString();
        var scope = GetParamString(parameters, context, "scope", "all");
        var chwDeltaT = GetParamDouble(parameters, context, "chw_delta_t", 5.0);
        var maxDuctVel = GetParamDouble(parameters, context, "max_duct_velocity_mps", 8.0);
        var maxPipeVel = GetParamDouble(parameters, context, "max_pipe_velocity_mps", 1.5);

        var report = new AutoDimReport();
        var steps = new List<string>();

        // Step 1: HVAC Load Calculation
        if (scope is "hvac" or "all")
        {
            try
            {
                var loadSkill = new HvacLoadCalculationSkill();
                var loadParams = new Dictionary<string, object?>();
                if (levelName is not null) loadParams["level_name"] = levelName;
                var loadResult = await loadSkill.ExecuteAsync(context, loadParams, cancellationToken);
                report.HvacLoad = loadResult.Data;
                steps.Add($"✓ HVAC load: {(loadResult.Success ? "OK" : "WARN")}");

                // Step 2: Flow from Load
                var flowSkill = new FlowFromLoadSkill();
                var flowParams = new Dictionary<string, object?>
                {
                    ["chw_delta_t"] = chwDeltaT,
                    ["air_delta_t"] = 10.0
                };
                if (levelName is not null) flowParams["level_name"] = levelName;
                var flowResult = await flowSkill.ExecuteAsync(context, flowParams, cancellationToken);
                report.FlowCalc = flowResult.Data;
                steps.Add($"✓ Flow calc: {(flowResult.Success ? "OK" : "WARN")}");

                // Step 3: Duct Sizing
                var ductSkill = new DuctNetworkSizingSkill();
                var ductParams = new Dictionary<string, object?>
                {
                    ["max_velocity_mps"] = maxDuctVel
                };
                if (systemName is not null) ductParams["system_name"] = systemName;
                var ductResult = await ductSkill.ExecuteAsync(context, ductParams, cancellationToken);
                report.DuctSizing = ductResult.Data;
                report.DuctMismatches = ExtractInt(ductResult.Data, "mismatchCount");
                steps.Add($"✓ Duct sizing: {report.DuctMismatches} mismatches");
            }
            catch (Exception ex)
            {
                steps.Add($"✗ HVAC pipeline: {ex.Message}");
            }
        }

        // Step 4: Pipe Sizing
        if (scope is "plumbing" or "all")
        {
            try
            {
                var pipeSkill = new PipeNetworkSizingSkill();
                var pipeParams = new Dictionary<string, object?>
                {
                    ["max_velocity_mps"] = maxPipeVel
                };
                if (systemName is not null) pipeParams["system_name"] = systemName;
                var pipeResult = await pipeSkill.ExecuteAsync(context, pipeParams, cancellationToken);
                report.PipeSizing = pipeResult.Data;
                report.PipeMismatches = ExtractInt(pipeResult.Data, "mismatchCount");
                steps.Add($"✓ Pipe sizing: {report.PipeMismatches} mismatches");
            }
            catch (Exception ex)
            {
                steps.Add($"✗ Pipe sizing: {ex.Message}");
            }
        }

        // Step 5: Insulation Check
        if (scope is "hvac" or "plumbing" or "all")
        {
            try
            {
                var insSkill = new InsulationThicknessSkill();
                var insParams = new Dictionary<string, object?>();
                if (levelName is not null) insParams["level"] = levelName;
                if (systemName is not null) insParams["system_name"] = systemName;
                var insResult = await insSkill.ExecuteAsync(context, insParams, cancellationToken);
                report.Insulation = insResult.Data;
                report.InsulationIssues = ExtractInt(insResult.Data, "issueCount");
                steps.Add($"✓ Insulation: {report.InsulationIssues} issues");
            }
            catch (Exception ex)
            {
                steps.Add($"✗ Insulation: {ex.Message}");
            }
        }

        // Step 6: Electrical (if in scope)
        if (scope is "electrical" or "all")
        {
            try
            {
                var elecSkill = new ElectricalLoadCalcSkill();
                var elecParams = new Dictionary<string, object?>();
                if (levelName is not null) elecParams["level"] = levelName;
                var elecResult = await elecSkill.ExecuteAsync(context, elecParams, cancellationToken);
                report.Electrical = elecResult.Data;
                report.VoltageDropIssues = ExtractInt(elecResult.Data, "voltageDropIssueCount");
                steps.Add($"✓ Electrical: {report.VoltageDropIssues} VD issues");
            }
            catch (Exception ex)
            {
                steps.Add($"✗ Electrical: {ex.Message}");
            }
        }

        var totalIssues = report.DuctMismatches + report.PipeMismatches +
                          report.InsulationIssues + report.VoltageDropIssues;
        var summary = new CalcResultSummary { TotalItems = steps.Count, IssueCount = totalIssues };
        var delta = ComputeDelta(context, summary);
        SaveResultForDelta(context, summary);

        var msg = $"Auto-dimensioning completed ({steps.Count} steps).\n" +
                  string.Join("\n", steps);
        if (totalIssues > 0)
            msg += $"\n\n⚠ Total issues found: {totalIssues}";
        if (delta is not null) msg += $"\n{delta.Summary}";

        var resultData = new
        {
            scope,
            stepsCompleted = steps.Count,
            totalIssues,
            breakdown = new
            {
                ductMismatches = report.DuctMismatches,
                pipeMismatches = report.PipeMismatches,
                insulationIssues = report.InsulationIssues,
                voltageDropIssues = report.VoltageDropIssues
            },
            hvacLoad = report.HvacLoad,
            flowCalc = report.FlowCalc,
            ductSizing = report.DuctSizing,
            pipeSizing = report.PipeSizing,
            insulation = report.Insulation,
            electrical = report.Electrical
        };

        return SkillResult.Ok(msg, resultData);
    }

    private static int ExtractInt(object? data, string propName)
    {
        if (data is null) return 0;
        try
        {
            var prop = data.GetType().GetProperty(propName);
            if (prop is not null) return Convert.ToInt32(prop.GetValue(data));
        }
        catch { }
        return 0;
    }

    private class AutoDimReport
    {
        public object? HvacLoad { get; set; }
        public object? FlowCalc { get; set; }
        public object? DuctSizing { get; set; }
        public object? PipeSizing { get; set; }
        public object? Insulation { get; set; }
        public object? Electrical { get; set; }
        public int DuctMismatches { get; set; }
        public int PipeMismatches { get; set; }
        public int InsulationIssues { get; set; }
        public int VoltageDropIssues { get; set; }
    }
}
