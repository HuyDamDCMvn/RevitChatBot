using Autodesk.Revit.DB;
using Nice3point.Revit.Extensions;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("map_parameters",
    "Copy or map parameter values from one parameter to another across multiple elements. " +
    "Useful for populating shared parameters, transferring data between fields, or " +
    "applying formulas. Inspired by DiStem Properties Mapping.")]
[SkillParameter("source_parameter", "string",
    "Name of the source parameter to read from.",
    isRequired: true)]
[SkillParameter("target_parameter", "string",
    "Name of the target parameter to write to.",
    isRequired: true)]
[SkillParameter("category", "string",
    "Category of elements: 'ducts', 'pipes', 'equipment', 'fittings', 'electrical', 'plumbing', 'all_mep'.",
    isRequired: true,
    allowedValues: new[] { "ducts", "pipes", "equipment", "fittings", "electrical", "plumbing", "all_mep" })]
[SkillParameter("transform", "string",
    "Optional transformation: 'copy' (direct copy), 'prefix:TEXT' (add prefix), " +
    "'suffix:TEXT' (add suffix), 'upper' (uppercase), 'lower' (lowercase), " +
    "'format:{0}_rev' (format string with {0} as source value). Default 'copy'.",
    isRequired: false)]
[SkillParameter("action", "string",
    "'preview' to show changes without applying, 'apply' to execute.",
    isRequired: true,
    allowedValues: new[] { "preview", "apply" })]
[SkillParameter("level_filter", "string",
    "Optional level name to restrict scope.",
    isRequired: false)]
[SkillParameter("source", "string",
    "'all' (default) to process all elements in category, 'selected' to only process currently selected elements in Revit.",
    isRequired: false, allowedValues: new[] { "all", "selected" })]
public class MapParametersSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory[]> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ducts"] = [BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_FlexDuctCurves],
        ["pipes"] = [BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_FlexPipeCurves],
        ["equipment"] = [BuiltInCategory.OST_MechanicalEquipment],
        ["fittings"] = [BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_PipeFitting],
        ["electrical"] = [BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures],
        ["plumbing"] = [BuiltInCategory.OST_PlumbingFixtures],
        ["all_mep"] = [
            BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_PlumbingFixtures
        ],
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var sourceParam = parameters.GetValueOrDefault("source_parameter")?.ToString();
        var targetParam = parameters.GetValueOrDefault("target_parameter")?.ToString();
        var category = parameters.GetValueOrDefault("category")?.ToString() ?? "all_mep";
        var transform = parameters.GetValueOrDefault("transform")?.ToString() ?? "copy";
        var action = parameters.GetValueOrDefault("action")?.ToString() ?? "preview";
        var levelFilter = parameters.GetValueOrDefault("level_filter")?.ToString();
        var source = parameters.GetValueOrDefault("source")?.ToString() ?? "all";

        if (string.IsNullOrWhiteSpace(sourceParam))
            return SkillResult.Fail("source_parameter is required.");
        if (string.IsNullOrWhiteSpace(targetParam))
            return SkillResult.Fail("target_parameter is required.");

        List<long>? selectionIds = null;
        if (source == "selected")
        {
            selectionIds = context.GetCurrentSelectionIds();
            if (selectionIds is null || selectionIds.Count == 0)
                return SkillResult.Fail("No elements currently selected in Revit.");
        }
        else if (!CategoryMap.TryGetValue(category, out _))
        {
            return SkillResult.Fail($"Unknown category '{category}'.");
        }

        CategoryMap.TryGetValue(category, out var bics);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var elements = new List<Element>();

            if (source == "selected" && selectionIds is not null)
            {
                foreach (var id in selectionIds)
                {
                    var elem = document.GetElement(new ElementId(id));
                    if (elem is not null)
                        elements.Add(elem);
                }
            }
            else if (bics is not null)
            {
                foreach (var bic in bics)
                    elements.AddRange(document.GetInstances(bic));
            }

            if (!string.IsNullOrWhiteSpace(levelFilter))
            {
                var levelIds = document.EnumerateInstances<Level>()
                    .Where(l => l.Name.Contains(levelFilter, StringComparison.OrdinalIgnoreCase))
                    .Select(l => l.Id.Value)
                    .ToHashSet();

                elements = elements.Where(e =>
                    e.LevelId is { } lid && lid != ElementId.InvalidElementId && levelIds.Contains(lid.Value)
                ).ToList();
            }

            var mappings = new List<MappingRecord>();
            var skipped = 0;

            foreach (var elem in elements)
            {
                var src = elem.FindParameter(sourceParam);
                var tgt = elem.FindParameter(targetParam);

                if (src is null || !src.HasValue) { skipped++; continue; }
                if (tgt is null || tgt.IsReadOnly) { skipped++; continue; }

                var sourceValue = src.AsValueString() ?? src.AsString() ?? "";
                if (string.IsNullOrEmpty(sourceValue)) { skipped++; continue; }

                var newValue = ApplyTransform(sourceValue, transform);
                var currentTarget = tgt.AsValueString() ?? tgt.AsString() ?? "";

                if (newValue == currentTarget) { skipped++; continue; }

                mappings.Add(new MappingRecord
                {
                    ElementId = elem.Id.Value,
                    ElementName = elem.Name,
                    SourceValue = sourceValue,
                    CurrentTargetValue = currentTarget,
                    NewTargetValue = newValue
                });
            }

            if (action == "apply" && mappings.Count > 0)
            {
                var applied = 0;
                var failed = 0;

                using var tx = new Transaction(document, "Map parameters");
                tx.Start();

                foreach (var mapping in mappings)
                {
                    var elem = new ElementId(mapping.ElementId).ToElement(document);
                    var tgt = elem?.FindParameter(targetParam);
                    if (tgt is null) { failed++; continue; }

                    if (SetParam(tgt, mapping.NewTargetValue))
                        applied++;
                    else
                        failed++;
                }

                tx.Commit();

                return new
                {
                    action = "apply",
                    appliedCount = applied,
                    failedCount = failed,
                    skippedCount = skipped,
                    totalElements = elements.Count,
                    details = mappings.Take(30).ToList()
                };
            }

            return new
            {
                action = "preview",
                appliedCount = 0,
                failedCount = 0,
                skippedCount = skipped,
                totalElements = elements.Count,
                mappingCount = mappings.Count,
                details = mappings.Take(30).ToList()
            };
        });

        dynamic res = result!;
        if (action == "apply")
            return SkillResult.Ok(
                $"Mapped '{sourceParam}' → '{targetParam}': {res.appliedCount} applied, {res.failedCount} failed.", result);

        return SkillResult.Ok(
            $"Preview: {res.mappingCount} elements would be updated ('{sourceParam}' → '{targetParam}').", result);
    }

    private static string ApplyTransform(string sourceValue, string transform)
    {
        if (transform == "copy") return sourceValue;
        if (transform == "upper") return sourceValue.ToUpperInvariant();
        if (transform == "lower") return sourceValue.ToLowerInvariant();
        if (transform.StartsWith("prefix:")) return transform[7..] + sourceValue;
        if (transform.StartsWith("suffix:")) return sourceValue + transform[7..];
        if (transform.Contains("{0}")) return transform.Replace("{0}", sourceValue);
        return sourceValue;
    }

    private static bool SetParam(Parameter param, string value)
    {
        try
        {
            return param.StorageType switch
            {
                StorageType.String => Do(() => param.Set(value)),
                StorageType.Double => double.TryParse(value, out var d) && Do(() => param.Set(d)),
                StorageType.Integer => int.TryParse(value, out var i) && Do(() => param.Set(i)),
                _ => false
            };
        }
        catch { return false; }
    }

    private static bool Do(Action action) { action(); return true; }

    private class MappingRecord
    {
        public long ElementId { get; set; }
        public string ElementName { get; set; } = "";
        public string SourceValue { get; set; } = "";
        public string CurrentTargetValue { get; set; } = "";
        public string NewTargetValue { get; set; } = "";
    }
}
