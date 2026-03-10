using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Query;

/// <summary>
/// Provides a comprehensive overview of all MEP systems in the model.
/// </summary>
[Skill("mep_system_overview",
    "Get a comprehensive overview of all MEP systems in the current Revit model. " +
    "Shows system counts, types, and element statistics for mechanical, plumbing, and electrical.")]
[SkillParameter("discipline", "string",
    "Filter by MEP discipline", isRequired: false,
    allowedValues: new[] { "mechanical", "plumbing", "electrical", "all" })]
public class SystemOverviewSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var discipline = parameters.GetValueOrDefault("discipline")?.ToString() ?? "all";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var overview = new Dictionary<string, object>();

            if (discipline is "all" or "mechanical")
            {
                var mechSystems = new FilteredElementCollector(document)
                    .OfClass(typeof(MechanicalSystem)).Cast<MechanicalSystem>().ToList();
                var ducts = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_DuctCurves).WhereElementIsNotElementType().GetElementCount();
                var ductFittings = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_DuctFitting).WhereElementIsNotElementType().GetElementCount();
                var mechEquip = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_MechanicalEquipment).WhereElementIsNotElementType().GetElementCount();

                overview["mechanical"] = new
                {
                    systemCount = mechSystems.Count,
                    systems = mechSystems.Select(s => new
                    {
                        name = s.Name,
                        type = s.SystemType.ToString(),
                        elements = s.DuctNetwork?.Size ?? 0
                    }).ToList(),
                    ductCount = ducts,
                    ductFittingCount = ductFittings,
                    equipmentCount = mechEquip
                };
            }

            if (discipline is "all" or "plumbing")
            {
                var pipeSystems = new FilteredElementCollector(document)
                    .OfClass(typeof(PipingSystem)).Cast<PipingSystem>().ToList();
                var pipes = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_PipeCurves).WhereElementIsNotElementType().GetElementCount();
                var pipeFittings = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_PipeFitting).WhereElementIsNotElementType().GetElementCount();
                var fixtures = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_PlumbingFixtures).WhereElementIsNotElementType().GetElementCount();

                overview["plumbing"] = new
                {
                    systemCount = pipeSystems.Count,
                    systems = pipeSystems.Select(s => new
                    {
                        name = s.Name,
                        type = s.SystemType.ToString(),
                        elements = s.PipingNetwork?.Size ?? 0
                    }).ToList(),
                    pipeCount = pipes,
                    pipeFittingCount = pipeFittings,
                    fixtureCount = fixtures
                };
            }

            if (discipline is "all" or "electrical")
            {
                var elecSystems = new FilteredElementCollector(document)
                    .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>().ToList();
                var panels = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_ElectricalEquipment).WhereElementIsNotElementType().GetElementCount();
                var conduits = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_Conduit).WhereElementIsNotElementType().GetElementCount();
                var cableTrays = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_CableTray).WhereElementIsNotElementType().GetElementCount();

                overview["electrical"] = new
                {
                    circuitCount = elecSystems.Count,
                    panelCount = panels,
                    conduitCount = conduits,
                    cableTrayCount = cableTrays
                };
            }

            return overview;
        });

        return SkillResult.Ok("MEP system overview retrieved.", result);
    }
}
