using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using RevitChatBot.Core.Skills;
using RevitChatBot.MEP.Skills.Calculation;

namespace RevitChatBot.MEP.Skills.Electrical;

/// <summary>
/// Checks electrical circuit loading and balance across panels.
/// Validates that circuits don't exceed rated capacity and that
/// panel loading is reasonably balanced across phases.
/// </summary>
[Skill("electrical_circuit_check",
    "Check electrical circuit loading and panel balance. Validates that circuits " +
    "don't exceed rated capacity, panels aren't overloaded, and phase loading " +
    "is balanced within tolerance.")]
[SkillParameter("max_load_percent", "number",
    "Maximum allowed load as percentage of circuit rating. Default: 80.",
    isRequired: false)]
[SkillParameter("max_phase_imbalance_percent", "number",
    "Maximum allowed phase imbalance percentage. Default: 15.",
    isRequired: false)]
[SkillParameter("level", "string",
    "Filter by level name (optional).",
    isRequired: false)]
public class ElectricalCircuitCheckSkill : CalculationSkillBase
{
    protected override string SkillName => "electrical_circuit_check";

    public override async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var maxLoadPct = GetParamDouble(parameters, context, "max_load_percent", 80);
        var maxPhasePct = GetParamDouble(parameters, context, "max_phase_imbalance_percent", 15);
        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var circuits = new FilteredElementCollector(document)
                .OfClass(typeof(ElectricalSystem))
                .Cast<ElectricalSystem>()
                .ToList();

            var panels = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .ToList();

            if (!string.IsNullOrWhiteSpace(levelFilter))
            {
                panels = panels.Where(p => GetLevelName(document, p)
                    .Contains(levelFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            var circuitIssues = new List<object>();
            int overloaded = 0;

            foreach (var circuit in circuits)
            {
                var apparentLoad = circuit.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD)?.AsDouble() ?? 0;
                var rating = circuit.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_RATING_PARAM)?.AsDouble() ?? 0;

                if (rating <= 0) continue;

                var loadPercent = (apparentLoad / rating) * 100;
                if (loadPercent > maxLoadPct)
                {
                    overloaded++;
                    circuitIssues.Add(new
                    {
                        circuitId = circuit.Id.Value,
                        circuitNumber = circuit.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER)?.AsString() ?? "N/A",
                        panelName = circuit.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_PANEL_PARAM)?.AsString() ?? "N/A",
                        apparentLoadVA = Math.Round(apparentLoad, 0),
                        ratingVA = Math.Round(rating, 0),
                        loadPercent = Math.Round(loadPercent, 1),
                        status = "OVERLOADED"
                    });
                }
            }

            var panelSummaries = new List<object>();
            foreach (var panel in panels)
            {
                var totalLoad = panel.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD)?.AsDouble() ?? 0;
                var panelName2 = panel.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()
                    ?? panel.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString()
                    ?? "Unknown";

                panelSummaries.Add(new
                {
                    panelName = panelName2,
                    elementId = panel.Id.Value,
                    totalLoadVA = Math.Round(totalLoad, 0),
                    level = GetLevelName(document, panel),
                    circuitCount = circuits.Count(c =>
                        c.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_PANEL_PARAM)?.AsString() == panelName2)
                });
            }

            return new
            {
                totalCircuits = circuits.Count,
                totalPanels = panels.Count,
                overloadedCircuits = overloaded,
                maxLoadThresholdPercent = maxLoadPct,
                circuitIssues = circuitIssues.Take(30).ToList(),
                panelSummaries = panelSummaries.OrderByDescending(p => ((dynamic)p!).totalLoadVA).ToList()
            };
        });

        var totalCircuits = (int)((dynamic)result!).totalCircuits;
        var overloadedCount = (int)((dynamic)result!).overloadedCircuits;
        var calcSummary = new CalcResultSummary { TotalItems = totalCircuits, IssueCount = overloadedCount };
        var delta = ComputeDelta(context, calcSummary);
        SaveResultForDelta(context, calcSummary);

        var msg = "Electrical circuit check completed.";
        if (delta is not null) msg += $"\n{delta.Summary}";

        var followUps = new List<FollowUpSuggestion>();
        if (overloadedCount > 0)
            followUps.Add(new FollowUpSuggestion
            {
                SkillName = "electrical_load_analysis",
                Reason = $"{overloadedCount} overloaded circuit(s) — review panel load distribution"
            });

        msg = AppendFollowUps(msg, followUps);
        return OkPaginated(msg, result, totalCircuits,
            Math.Min(totalCircuits, 30), "circuits");
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var lvlId = elem.LevelId;
        if (lvlId is null || lvlId == ElementId.InvalidElementId) return "";
        return doc.GetElement(lvlId)?.Name ?? "";
    }
}
