using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("modify_parameter",
    "Modify a parameter value on a Revit element. " +
    "Specify the element ID, parameter name, and new value.")]
[SkillParameter("element_id", "integer", "The Revit element ID to modify", isRequired: true)]
[SkillParameter("parameter_name", "string", "The parameter name to change", isRequired: true)]
[SkillParameter("new_value", "string", "The new value to set", isRequired: true)]
public class ModifyParameterSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        if (!TryGetLong(parameters, "element_id", out var elementIdValue))
            return SkillResult.Fail("Invalid element_id.");

        var paramName = parameters.GetValueOrDefault("parameter_name")?.ToString();
        var newValue = parameters.GetValueOrDefault("new_value")?.ToString();

        if (string.IsNullOrWhiteSpace(paramName) || newValue is null)
            return SkillResult.Fail("parameter_name and new_value are required.");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elementId = new ElementId(elementIdValue);
            var service = new RevitElementService();

            var element = document.GetElement(elementId);
            if (element is null)
                return new { success = false, reason = "Element not found" };

            var oldParams = service.GetElementParameters(element);
            var oldValue = oldParams.GetValueOrDefault(paramName!, "N/A");

            var modified = service.SetElementParameter(document, elementId, paramName!, newValue!);

            return new
            {
                success = modified,
                reason = modified ? "Parameter updated" : "Failed to set parameter (read-only or wrong type)",
                elementName = element.Name,
                parameterName = paramName,
                oldValue,
                newValue
            };
        });

        var data = result as dynamic;
        if (data?.success == true)
            return SkillResult.Ok($"Updated '{paramName}' on element {elementIdValue}.", result);

        return SkillResult.Fail($"Failed to modify parameter: {data?.reason}", null);
    }

    private static bool TryGetLong(Dictionary<string, object?> p, string key, out long value)
    {
        value = 0;
        if (!p.TryGetValue(key, out var v) || v is null) return false;
        return long.TryParse(v.ToString(), out value);
    }
}
