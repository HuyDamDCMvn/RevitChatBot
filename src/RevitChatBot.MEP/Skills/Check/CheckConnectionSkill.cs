using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Nice3point.Revit.Extensions;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("check_disconnected_mep",
    "Check disconnected MEP elements. Iterates ducts, pipes, flex ducts, flex pipes, fittings, " +
    "accessories, equipment, and terminals. Returns list of elements with unconnected connectors.")]
public class CheckConnectionSkill : ISkill
{
    private static readonly (string Label, BuiltInCategory Cat)[] MepCategories =
    {
        ("Ducts", BuiltInCategory.OST_DuctCurves),
        ("Pipes", BuiltInCategory.OST_PipeCurves),
        ("Flex Ducts", BuiltInCategory.OST_FlexDuctCurves),
        ("Flex Pipes", BuiltInCategory.OST_FlexPipeCurves),
        ("Duct Fittings", BuiltInCategory.OST_DuctFitting),
        ("Pipe Fittings", BuiltInCategory.OST_PipeFitting),
        ("Duct Accessories", BuiltInCategory.OST_DuctAccessory),
        ("Pipe Accessories", BuiltInCategory.OST_PipeAccessory),
        ("Mechanical Equipment", BuiltInCategory.OST_MechanicalEquipment),
        ("Duct Terminals", BuiltInCategory.OST_DuctTerminal),
        ("Plumbing Fixtures", BuiltInCategory.OST_PlumbingFixtures)
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var disconnected = new List<object>();

            foreach (var (label, category) in MepCategories)
            {
                var elements = document.GetInstances(category).ToList();

                foreach (var elem in elements)
                {
                    ConnectorManager? cm = null;
                    if (elem is MEPCurve curve)
                        cm = curve.ConnectorManager;
                    else if (elem is FamilyInstance fi && fi.MEPModel is { } mepModel)
                        cm = mepModel.ConnectorManager;

                    if (cm is null) continue;

                    var hasUnconnected = false;
                    foreach (Connector c in cm.Connectors)
                    {
                        if (!c.IsConnected)
                        {
                            hasUnconnected = true;
                            break;
                        }
                    }

                    if (!hasUnconnected) continue;

                    var typeName = elem.GetTypeId().ToElement(document)?.Name ?? "N/A";
                    disconnected.Add(new
                    {
                        elementId = elem.Id.Value,
                        category = label,
                        name = elem.Name,
                        typeName
                    });
                }
            }

            return new
            {
                disconnectedCount = disconnected.Count,
                disconnected
            };
        });

        return SkillResult.Ok("MEP connection check completed.", result);
    }
}
