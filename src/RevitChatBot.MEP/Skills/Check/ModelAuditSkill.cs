using Autodesk.Revit.DB;
using Nice3point.Revit.Extensions;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("model_audit",
    "Model audit/overview. Counts warnings from doc.GetWarnings(), groups top 20 by description, " +
    "counts total elements and MEP elements by category. Returns audit summary.")]
public class ModelAuditSkill : ISkill
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
        ("Electrical Equipment", BuiltInCategory.OST_ElectricalEquipment),
        ("Plumbing Fixtures", BuiltInCategory.OST_PlumbingFixtures),
        ("Sprinklers", BuiltInCategory.OST_Sprinklers)
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

            var warnings = document.GetWarnings();
            var warningDescriptions = new List<string>();
            foreach (FailureMessage fm in warnings)
            {
                if (fm?.GetDescriptionText() is { } desc)
                    warningDescriptions.Add(desc);
            }

            var topWarnings = warningDescriptions
                .GroupBy(d => d)
                .OrderByDescending(g => g.Count())
                .Take(20)
                .Select(g => new { description = g.Key, count = g.Count() })
                .ToList();

            var totalElements = document.GetElements().WhereElementIsNotElementType().GetElementCount();

            var mepCounts = new List<object>();
            foreach (var (label, category) in MepCategories)
            {
                var count = document.GetInstances(category).Count;
                mepCounts.Add(new { category = label, count });
            }

            return new
            {
                totalWarnings = warningDescriptions.Count,
                topWarnings,
                totalElements,
                mepCounts
            };
        });

        return SkillResult.Ok("Model audit completed.", result);
    }
}
