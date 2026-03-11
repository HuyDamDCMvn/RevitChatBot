using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("check_parameter_completeness",
    "Check parameter completeness. Collects elements of specified category, checks if each has the " +
    "parameter and a non-empty value. Returns filled/missing counts and completion rate.")]
[SkillParameter("parameterName", "string",
    "The parameter name or BuiltInParameter to check", isRequired: true)]
[SkillParameter("category", "string",
    "Element category: ducts, pipes (default: ducts)", isRequired: false,
    allowedValues: new[] { "ducts", "pipes" })]
[SkillParameter("scope", "string",
    "Scope: 'active_view' to check only elements visible in the current view, " +
    "'entire_model' to check all (default: entire_model)",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class ComplianceCheckSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var paramName = parameters.GetValueOrDefault("parameterName")?.ToString();
        if (string.IsNullOrWhiteSpace(paramName))
            return SkillResult.Fail("Parameter 'parameterName' is required.");

        var category = parameters.GetValueOrDefault("category")?.ToString() ?? "ducts";
        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elements = category switch
            {
                "pipes" => ViewScopeHelper.CreateCollector(document, scope)
                    .OfClass(typeof(Pipe))
                    .WhereElementIsNotElementType()
                    .Cast<Element>()
                    .ToList(),
                _ => ViewScopeHelper.CreateCollector(document, scope)
                    .OfClass(typeof(Duct))
                    .WhereElementIsNotElementType()
                    .Cast<Element>()
                    .ToList()
            };

            var filled = 0;
            var missing = 0;
            var missingDetails = new List<object>();

            foreach (var elem in elements)
            {
                Parameter? param = null;
                if (Enum.TryParse<BuiltInParameter>(paramName, out var bip))
                    param = elem.get_Parameter(bip);
                param ??= elem.LookupParameter(paramName);

                var hasValue = false;
                if (param is not null)
                {
                    if (param.StorageType == StorageType.String)
                        hasValue = !string.IsNullOrWhiteSpace(param.AsString());
                    else if (param.StorageType == StorageType.Integer)
                        hasValue = param.AsInteger() != 0 || param.HasValue;
                    else if (param.StorageType == StorageType.Double)
                        hasValue = Math.Abs(param.AsDouble()) > 1e-9 || param.HasValue;
                    else if (param.StorageType == StorageType.ElementId)
                        hasValue = param.AsElementId() is { } eid && eid != ElementId.InvalidElementId;
                }

                if (hasValue)
                    filled++;
                else
                {
                    missing++;
                    if (missingDetails.Count < 50)
                    {
                        var size = elem.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";
                        missingDetails.Add(new { elementId = elem.Id.Value, size });
                    }
                }
            }

            var total = filled + missing;
            var completionRate = total > 0 ? Math.Round(100.0 * filled / total, 1) : 0;

            return new
            {
                parameterName = paramName,
                category,
                totalElements = total,
                filled,
                missing,
                completionRate,
                missingDetails
            };
        });

        return SkillResult.Ok("Parameter completeness check completed.", result);
    }
}
