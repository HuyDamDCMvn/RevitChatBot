using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Electrical;

/// <summary>
/// Analyzes electrical panels and circuits: load distribution, circuit counts, and capacity.
/// </summary>
[Skill("electrical_load_analysis",
    "Analyze electrical load distribution across panels and circuits. " +
    "Returns panel schedules, circuit loads, and capacity utilization.")]
[SkillParameter("panel_name", "string", "Filter by panel name (optional)", isRequired: false)]
[SkillParameter("include_circuits", "string",
    "Include detailed circuit info (default: false)", isRequired: false,
    allowedValues: new[] { "true", "false" })]
public class ElectricalLoadSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var panelName = parameters.GetValueOrDefault("panel_name")?.ToString();
        var includeCircuits = parameters.GetValueOrDefault("include_circuits")?.ToString() == "true";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var panels = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .ToList();

            if (panelName is not null)
                panels = panels.Where(p =>
                    p.Name?.Contains(panelName, StringComparison.OrdinalIgnoreCase) == true).ToList();

            var panelData = panels.Select(panel =>
            {
                var totalLoad = panel.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_TOTALLOAD_PARAM)?.AsDouble() ?? 0;
                var totalConnected = panel.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_TOTALESTLOAD_PARAM)?.AsDouble() ?? 0;

                var circuits = new FilteredElementCollector(document)
                    .OfClass(typeof(ElectricalSystem))
                    .Cast<ElectricalSystem>()
                    .Where(c => c.BaseEquipment?.Id == panel.Id)
                    .ToList();

                var circuitData = includeCircuits
                    ? circuits.Select(c => new
                    {
                        name = c.Name,
                        circuitNumber = c.CircuitNumber,
                        voltage = c.Voltage,
                        apparentLoad = c.ApparentLoad,
                        poleCount = c.PolesNumber
                    }).ToList()
                    : null;

                return new
                {
                    id = panel.Id.Value,
                    name = panel.Name,
                    level = (document.GetElement(panel.LevelId) as Level)?.Name ?? "N/A",
                    totalLoadVA = Math.Round(totalLoad, 2),
                    totalLoadKW = Math.Round(totalLoad / 1000, 2),
                    connectedLoadVA = Math.Round(totalConnected, 2),
                    circuitCount = circuits.Count,
                    circuits = circuitData
                };
            }).ToList();

            var totalSystemLoad = panelData.Sum(p => p.totalLoadVA);

            return new
            {
                totalPanels = panelData.Count,
                totalSystemLoadVA = Math.Round(totalSystemLoad, 2),
                totalSystemLoadKW = Math.Round(totalSystemLoad / 1000, 2),
                panels = panelData
            };
        });

        return SkillResult.Ok("Electrical load analysis completed.", result);
    }
}
