using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("batch_modify", "Batch modify element parameters. Set a parameter value on multiple elements in one operation. " +
    "Supports targeting currently selected elements in Revit via source='selected'.")]
[SkillParameter("elementIds", "string",
    "Comma-separated element IDs to modify. Not required when source='selected'.",
    isRequired: false)]
[SkillParameter("parameterName", "string", "Parameter name to set", isRequired: true)]
[SkillParameter("value", "string", "Value to set", isRequired: true)]
[SkillParameter("source", "string",
    "'element_ids' (default) to use elementIds parameter, 'selected' to use currently selected elements in Revit.",
    isRequired: false, allowedValues: new[] { "element_ids", "selected" })]
public class BatchModifySkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var source = parameters.GetValueOrDefault("source")?.ToString() ?? "element_ids";
        var idsStr = parameters.GetValueOrDefault("elementIds")?.ToString();
        var paramName = parameters.GetValueOrDefault("parameterName")?.ToString();
        var valueStr = parameters.GetValueOrDefault("value")?.ToString();

        if (string.IsNullOrWhiteSpace(paramName))
            return SkillResult.Fail("parameterName is required.");
        if (valueStr is null)
            return SkillResult.Fail("value is required.");

        var elementIds = new List<long>();

        if (source == "selected")
        {
            var selIds = context.GetCurrentSelectionIds();
            if (selIds is null || selIds.Count == 0)
                return SkillResult.Fail("No elements currently selected in Revit.");
            elementIds.AddRange(selIds);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(idsStr))
                return SkillResult.Fail("elementIds is required when source is 'element_ids'.");

            foreach (var s in idsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (long.TryParse(s, out var id))
                    elementIds.Add(id);
            }
        }

        if (elementIds.Count == 0)
            return SkillResult.Fail("No valid element IDs resolved.");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var successCount = 0;
            var failedCount = 0;
            var failedIds = new List<long>();

            using var tx = new Transaction(document, "Batch modify parameters");
            tx.Start();

            foreach (var id in elementIds)
            {
                var element = document.GetElement(new ElementId(id));
                if (element is null)
                {
                    failedCount++;
                    failedIds.Add(id);
                    continue;
                }

                var param = element.LookupParameter(paramName);
                if (param is null || param.IsReadOnly)
                {
                    failedCount++;
                    failedIds.Add(id);
                    continue;
                }

                var ok = SetParameterValue(param, valueStr);
                if (ok)
                    successCount++;
                else
                {
                    failedCount++;
                    failedIds.Add(id);
                }
            }

            tx.Commit();

            return new
            {
                success_count = successCount,
                failed_count = failedCount,
                failed_ids = failedIds
            };
        });

        return SkillResult.Ok($"Batch modify completed: {((dynamic)result!).success_count} succeeded, {((dynamic)result).failed_count} failed.", result);
    }

    private static bool SetParameterValue(Parameter param, string valueStr)
    {
        try
        {
            return param.StorageType switch
            {
                StorageType.String => TrySetString(param, valueStr),
                StorageType.Double => TrySetDouble(param, valueStr),
                StorageType.Integer => TrySetInteger(param, valueStr),
                StorageType.ElementId => false,
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetString(Parameter param, string value)
    {
        param.Set(value);
        return true;
    }

    private static bool TrySetDouble(Parameter param, string valueStr)
    {
        if (!double.TryParse(valueStr, out var d)) return false;
        param.Set(d);
        return true;
    }

    private static bool TrySetInteger(Parameter param, string valueStr)
    {
        if (!int.TryParse(valueStr, out var i)) return false;
        param.Set(i);
        return true;
    }
}
