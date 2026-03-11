using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Calculation;

/// <summary>
/// Advanced electrical load calculation with demand factors, voltage drop estimation,
/// and transformer sizing recommendations. Goes beyond the existing ElectricalLoadSkill
/// by applying NEC demand factors and calculating voltage drop.
/// </summary>
[Skill("electrical_load_calc",
    "Advanced electrical load calculation with NEC demand factors, voltage drop estimation, " +
    "and transformer sizing. Calculates total connected load, demand load with diversity, " +
    "and checks voltage drop across circuit lengths.")]
[SkillParameter("demand_factor", "number",
    "Overall demand factor (0-1). Default: 0.65 for commercial buildings.", isRequired: false)]
[SkillParameter("voltage", "number",
    "System voltage (default: 380V for 3-phase)", isRequired: false)]
[SkillParameter("max_voltage_drop_percent", "number",
    "Maximum allowed voltage drop %. Default: 3 for branch, 5 total.", isRequired: false)]
[SkillParameter("level", "string", "Filter by level name (optional)", isRequired: false)]
public class ElectricalLoadCalcSkill : CalculationSkillBase
{
    protected override string SkillName => "electrical_load_calc";

    public override async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var demandFactor = GetParamDouble(parameters, context, "demand_factor", 0.65);
        var systemVoltage = GetParamDouble(parameters, context, "voltage", 380);
        var maxVdPct = GetParamDouble(parameters, context, "max_voltage_drop_percent", 3);
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var panels = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .ToList();

            if (!string.IsNullOrWhiteSpace(levelFilter))
                panels = panels.Where(p =>
                    GetLevelName(document, p).Contains(levelFilter!, StringComparison.OrdinalIgnoreCase)).ToList();

            var circuits = new FilteredElementCollector(document)
                .OfClass(typeof(ElectricalSystem))
                .Cast<ElectricalSystem>()
                .ToList();

            var panelResults = panels.Select(panel =>
            {
                var connectedVA = panel.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_TOTALLOAD_PARAM)?.AsDouble() ?? 0;
                var demandVA = connectedVA * demandFactor;
                var demandKW = demandVA / 1000;

                // Current: I = P / (V × PF × √3) for 3-phase
                var pf = 0.85;
                var currentA = (systemVoltage > 0)
                    ? demandVA / (systemVoltage * pf * Math.Sqrt(3))
                    : 0;

                var panelCircuits = circuits
                    .Where(c => c.BaseEquipment?.Id == panel.Id)
                    .ToList();

                var vdIssues = new List<object>();
                foreach (var circuit in panelCircuits)
                {
                    var circuitLoad = circuit.ApparentLoad;
                    var circuitVoltage = circuit.Voltage > 0 ? circuit.Voltage : systemVoltage;
                    var circuitCurrent = circuitVoltage > 0 ? circuitLoad / circuitVoltage : 0;

                    var lengthFt = circuit.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_LENGTH_PARAM)?.AsDouble() ?? 0;
                    var lengthM = lengthFt * 0.3048;

                    // Simplified voltage drop: Vd% = (2 × I × L × R) / V × 100
                    // R ≈ 0.04 Ω/m for typical copper conductor
                    var resistance = 0.04;
                    var vdPercent = circuitVoltage > 0
                        ? (2 * circuitCurrent * lengthM * resistance) / circuitVoltage * 100
                        : 0;

                    if (vdPercent > maxVdPct)
                    {
                        vdIssues.Add(new
                        {
                            circuitName = circuit.Name,
                            circuitNumber = circuit.CircuitNumber,
                            loadVA = Math.Round(circuitLoad, 0),
                            lengthM = Math.Round(lengthM, 1),
                            voltageDropPercent = Math.Round(vdPercent, 2),
                            maxAllowed = maxVdPct,
                            status = "VOLTAGE_DROP_EXCEEDED"
                        });
                    }
                }

                return new
                {
                    id = panel.Id.Value,
                    name = panel.Name,
                    level = GetLevelName(document, panel),
                    connectedLoadVA = Math.Round(connectedVA, 0),
                    connectedLoadKW = Math.Round(connectedVA / 1000, 2),
                    demandFactor,
                    demandLoadVA = Math.Round(demandVA, 0),
                    demandLoadKW = Math.Round(demandKW, 2),
                    estimatedCurrentA = Math.Round(currentA, 1),
                    circuitCount = panelCircuits.Count,
                    voltageDropIssues = vdIssues.Take(10).ToList(),
                    voltageDropIssueCount = vdIssues.Count
                };
            }).ToList();

            var totalConnected = panelResults.Sum(p => p.connectedLoadVA);
            var totalDemand = panelResults.Sum(p => p.demandLoadVA);
            var totalVdIssues = panelResults.Sum(p => p.voltageDropIssueCount);

            // Transformer sizing recommendation
            var txKva = totalDemand / 1000 * 1.25; // 25% safety margin
            var standardTxSizes = new[] { 100, 160, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600, 2000, 2500 };
            var recommendedTx = standardTxSizes.FirstOrDefault(s => s >= txKva);
            if (recommendedTx == 0) recommendedTx = standardTxSizes[^1];

            return new
            {
                totalPanels = panelResults.Count,
                voltageDropIssueCount = totalVdIssues,
                designParameters = new
                {
                    systemVoltage,
                    demandFactor,
                    maxVoltageDropPercent = maxVdPct,
                    powerFactor = 0.85
                },
                loadSummary = new
                {
                    totalConnectedKVA = Math.Round(totalConnected / 1000, 1),
                    totalDemandKVA = Math.Round(totalDemand / 1000, 1),
                    totalDemandKW = Math.Round(totalDemand * 0.85 / 1000, 1),
                    recommendedTransformerKVA = recommendedTx
                },
                panels = panelResults
            };
        });

        var totalPanels = (int)((dynamic)result!).totalPanels;
        var vdIssues = (int)((dynamic)result!).voltageDropIssueCount;
        var calcSummary = new CalcResultSummary { TotalItems = totalPanels, IssueCount = vdIssues };
        var delta = ComputeDelta(context, calcSummary);
        SaveResultForDelta(context, calcSummary);

        var msg = "Advanced electrical load calculation completed.";
        if (delta is not null) msg += $"\n{delta.Summary}";

        var followUps = new List<FollowUpSuggestion>();
        if (vdIssues > 0)
            followUps.Add(new FollowUpSuggestion
            {
                SkillName = "electrical_circuit_check",
                Reason = $"{vdIssues} voltage drop issue(s) — check circuit loading"
            });

        msg = AppendFollowUps(msg, followUps);
        return OkPaginated(msg, result, totalPanels, Math.Min(totalPanels, 20), "panels");
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var lvlId = elem.LevelId;
        if (lvlId is null || lvlId == ElementId.InvalidElementId) return "";
        return doc.GetElement(lvlId)?.Name ?? "";
    }
}
