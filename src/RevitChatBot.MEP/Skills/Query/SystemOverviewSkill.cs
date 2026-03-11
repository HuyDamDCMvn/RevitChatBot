using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;
using Nice3point.Revit.Extensions;
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
                var mechSystems = document.GetElements().OfClass(typeof(MechanicalSystem)).Cast<MechanicalSystem>().ToList();
                var ducts = document.GetInstances(BuiltInCategory.OST_DuctCurves).Count;
                var ductFittings = document.GetInstances(BuiltInCategory.OST_DuctFitting).Count;
                var mechEquip = document.GetInstances(BuiltInCategory.OST_MechanicalEquipment).Count;

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
                var pipeSystems = document.GetElements().OfClass(typeof(PipingSystem)).Cast<PipingSystem>().ToList();
                var pipes = document.GetInstances(BuiltInCategory.OST_PipeCurves).Count;
                var pipeFittings = document.GetInstances(BuiltInCategory.OST_PipeFitting).Count;
                var fixtures = document.GetInstances(BuiltInCategory.OST_PlumbingFixtures).Count;

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
                var elecSystems = document.GetElements().OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>().ToList();
                var panels = document.GetInstances(BuiltInCategory.OST_ElectricalEquipment).Count;
                var conduits = document.GetInstances(BuiltInCategory.OST_Conduit).Count;
                var cableTrays = document.GetInstances(BuiltInCategory.OST_CableTray).Count;

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
