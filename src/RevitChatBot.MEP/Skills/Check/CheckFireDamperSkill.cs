using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("check_fire_dampers",
    "Check fire dampers in model. Collects duct accessories (OST_DuctAccessory), filters by family/type " +
    "name containing 'fire' or 'damper', checks if all connectors are connected. Returns damper list with connected status.")]
[SkillParameter("level", "string",
    "Optional level name to filter dampers by", isRequired: false)]
[SkillParameter("scope", "string",
    "Scope: 'active_view' to check only elements visible in the current view, " +
    "'entire_model' to check all (default: entire_model)",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class CheckFireDamperSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var accessories = ViewScopeHelper.CreateCollector(document, scope)
                .OfCategory(BuiltInCategory.OST_DuctAccessory)
                .WhereElementIsNotElementType()
                .ToList();

            var dampers = new List<object>();
            foreach (var elem in accessories)
            {
                var familyName = elem.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsString() ?? "";
                var typeName = document.GetElement(elem.GetTypeId())?.Name ?? "";
                var combined = $"{familyName} {typeName}".ToLowerInvariant();
                if (!combined.Contains("fire") && !combined.Contains("damper"))
                    continue;

                if (levelFilter is not null)
                {
                    var levelId = elem.LevelId ?? elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId();
                    var elemLevel = levelId is not null && levelId != ElementId.InvalidElementId
                        ? document.GetElement(levelId)?.Name
                        : null;
                    if (elemLevel is null || !elemLevel.Contains(levelFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                var allConnected = true;
                ConnectorManager? cm = null;
                if (elem is FamilyInstance fi && fi.MEPModel is { } mepModel)
                {
                    cm = mepModel.ConnectorManager;
                    if (cm is not null)
                    {
                        foreach (Connector c in cm.Connectors)
                        {
                            if (!c.IsConnected)
                            {
                                allConnected = false;
                                break;
                            }
                        }
                    }
                }

                dampers.Add(new
                {
                    elementId = elem.Id.Value,
                    familyName,
                    typeName,
                    allConnected
                });
            }

            return new
            {
                damperCount = dampers.Count,
                levelFilter = levelFilter ?? "(all)",
                dampers
            };
        });

        return SkillResult.Ok("Fire damper check completed.", result);
    }
}
