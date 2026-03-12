using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("check_shared_parameters",
    "Audit shared parameters in the project. Lists all shared parameter definitions, " +
    "checks which are bound to categories, finds parameters with no values, " +
    "and validates against expected parameter names from a BEP.")]
[SkillParameter("action", "string",
    "'list' to list all shared parameters, 'audit' to check bindings and fill rates, " +
    "'validate' to validate against expected names. Default: 'audit'.",
    isRequired: false,
    allowedValues: new[] { "list", "audit", "validate" })]
[SkillParameter("expected_parameters", "string",
    "Comma-separated parameter names expected per BEP (for action='validate'). " +
    "E.g. 'COBie.Type.Name,COBie.Space.Name,Status'. Optional.",
    isRequired: false)]
[SkillParameter("category", "string",
    "Filter to a specific category: 'ducts', 'pipes', 'equipment', etc. Default: all categories.",
    isRequired: false)]
public class SharedParameterCheckSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var action = parameters.GetValueOrDefault("action")?.ToString()?.ToLower() ?? "audit";
        var expectedStr = parameters.GetValueOrDefault("expected_parameters")?.ToString();
        var categoryFilter = parameters.GetValueOrDefault("category")?.ToString()?.ToLower();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var bindingMap = document.ParameterBindings;
            var iterator = bindingMap.ForwardIterator();
            var sharedParams = new List<Dictionary<string, object>>();

            while (iterator.MoveNext())
            {
                var definition = iterator.Key;
                var binding = iterator.Current;
                if (definition is null || binding is null) continue;

                var categories = new List<string>();
                if (binding is ElementBinding elemBinding)
                {
                    foreach (Category cat in elemBinding.Categories)
                        categories.Add(cat.Name);
                }

                var paramInfo = new Dictionary<string, object>
                {
                    ["name"] = definition.Name,
                    ["parameterGroup"] = definition.GetGroupTypeId()?.TypeId ?? "Unknown",
                    ["bindingType"] = binding is InstanceBinding ? "Instance" : "Type",
                    ["boundCategories"] = categories,
                    ["categoryCount"] = categories.Count,
                };

                if (definition is ExternalDefinition extDef)
                    paramInfo["guid"] = extDef.GUID.ToString();

                sharedParams.Add(paramInfo);
            }

            var report = new Dictionary<string, object>
            {
                ["totalSharedParameters"] = sharedParams.Count,
            };

            if (action is "list" or "audit")
            {
                report["parameters"] = sharedParams.OrderBy(p => p["name"]?.ToString()).ToList();
            }

            if (action is "audit")
            {
                var noCategoryParams = sharedParams.Where(p => (int)p["categoryCount"] == 0).Select(p => p["name"]).ToList();
                report["parametersWithNoCategories"] = noCategoryParams;
                report["noCategoryCount"] = noCategoryParams.Count;

                var fillRates = new List<object>();
                foreach (var sp in sharedParams.Take(50))
                {
                    var pName = sp["name"]?.ToString() ?? "";
                    var cats = (List<string>)sp["boundCategories"];
                    if (cats.Count == 0) continue;

                    int total = 0, filled = 0;
                    foreach (var catName in cats.Take(5))
                    {
                        var builtInCat = GetBuiltInCategory(catName);
                        if (builtInCat == BuiltInCategory.INVALID) continue;

                        try
                        {
                            var elements = new FilteredElementCollector(document)
                                .OfCategory(builtInCat)
                                .WhereElementIsNotElementType()
                                .ToElements()
                                .Take(100);

                            foreach (var elem in elements)
                            {
                                total++;
                                var param = elem.LookupParameter(pName);
                                if (param is not null && param.HasValue &&
                                    !string.IsNullOrWhiteSpace(param.AsValueString()))
                                    filled++;
                            }
                        }
                        catch { }
                    }

                    if (total > 0)
                    {
                        fillRates.Add(new
                        {
                            parameter = pName,
                            sampleSize = total,
                            fillRate = Math.Round(100.0 * filled / total, 1)
                        });
                    }
                }
                report["fillRates"] = fillRates.OrderBy(f => ((dynamic)f).fillRate).ToList();
            }

            if (action == "validate" && !string.IsNullOrWhiteSpace(expectedStr))
            {
                var expected = expectedStr!.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToList();
                var existingNames = sharedParams.Select(p => p["name"]?.ToString() ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missing = expected.Where(e => !existingNames.Contains(e)).ToList();
                var found = expected.Where(e => existingNames.Contains(e)).ToList();

                report["expectedParameters"] = expected;
                report["found"] = found;
                report["missing"] = missing;
                report["complianceRate"] = expected.Count > 0
                    ? Math.Round(100.0 * found.Count / expected.Count, 1)
                    : 100.0;
            }

            return report;
        });

        var data = (Dictionary<string, object>)result!;
        var summary = $"Shared parameter audit: {data["totalSharedParameters"]} parameters found.";
        if (data.ContainsKey("noCategoryCount"))
            summary += $" {data["noCategoryCount"]} have no category bindings.";
        if (data.ContainsKey("complianceRate"))
            summary += $" BEP compliance: {data["complianceRate"]}%.";
        return SkillResult.Ok(summary, result);
    }

    private static BuiltInCategory GetBuiltInCategory(string name)
    {
        var map = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ducts"] = BuiltInCategory.OST_DuctCurves,
            ["Pipes"] = BuiltInCategory.OST_PipeCurves,
            ["Mechanical Equipment"] = BuiltInCategory.OST_MechanicalEquipment,
            ["Electrical Equipment"] = BuiltInCategory.OST_ElectricalEquipment,
            ["Cable Trays"] = BuiltInCategory.OST_CableTray,
            ["Conduits"] = BuiltInCategory.OST_Conduit,
            ["Sprinklers"] = BuiltInCategory.OST_Sprinklers,
            ["Pipe Fittings"] = BuiltInCategory.OST_PipeFitting,
            ["Duct Fittings"] = BuiltInCategory.OST_DuctFitting,
        };
        return map.GetValueOrDefault(name, BuiltInCategory.INVALID);
    }
}
