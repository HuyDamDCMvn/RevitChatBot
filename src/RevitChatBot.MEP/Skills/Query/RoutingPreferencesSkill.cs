using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Query;

[Skill("query_routing_preferences",
    "Query routing preferences for pipe and duct types in the model. Shows preferred fittings " +
    "(elbows, tees, crosses, transitions), junction types (Tee vs Tap), and segment rules. " +
    "Useful for understanding which fittings Revit will auto-insert during routing.")]
[SkillParameter("category", "string",
    "Filter by type: pipe, duct, or all. Default: all",
    isRequired: false, allowedValues: ["pipe", "duct", "all"])]
[SkillParameter("type_name", "string",
    "Filter by specific type name (partial match, case-insensitive). Default: empty = all types",
    isRequired: false)]
public class RoutingPreferencesSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        string category = GetString(parameters, "category", "all");
        string typeName = GetString(parameters, "type_name", "");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var lines = new List<string> { "=== MEP Routing Preferences ===" };

            if (category is "pipe" or "all")
            {
                var pipeTypes = new FilteredElementCollector(document)
                    .OfClass(typeof(PipeType)).Cast<PipeType>().ToList();

                if (!string.IsNullOrEmpty(typeName))
                    pipeTypes = pipeTypes.Where(t =>
                        t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase)).ToList();

                lines.Add($"\n--- Pipe Types ({pipeTypes.Count}) ---");
                lines.Add("| Type | Junction | Segments | Elbows | Junctions | Crosses | Transitions |");
                lines.Add("|------|----------|----------|--------|-----------|---------|-------------|");

                foreach (var pt in pipeTypes)
                {
                    try
                    {
                        var rpm = pt.RoutingPreferenceManager;
                        string junc = rpm.PreferredJunctionType.ToString();
                        int seg = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments);
                        int elb = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Elbows);
                        int jun = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Junctions);
                        int crs = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Crosses);
                        int trn = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Transitions);
                        lines.Add($"| {pt.Name} | {junc} | {seg} | {elb} | {jun} | {crs} | {trn} |");
                    }
                    catch
                    {
                        lines.Add($"| {pt.Name} | Error reading | - | - | - | - | - |");
                    }
                }

                foreach (var pt in pipeTypes.Take(5))
                {
                    try
                    {
                        var rpm = pt.RoutingPreferenceManager;
                        lines.Add($"\n  Details for '{pt.Name}':");
                        AppendRuleDetails(document, rpm, RoutingPreferenceRuleGroupType.Elbows, "Elbows", lines);
                        AppendRuleDetails(document, rpm, RoutingPreferenceRuleGroupType.Junctions, "Junctions", lines);
                        AppendRuleDetails(document, rpm, RoutingPreferenceRuleGroupType.Crosses, "Crosses", lines);
                        AppendRuleDetails(document, rpm, RoutingPreferenceRuleGroupType.Transitions, "Transitions", lines);
                        AppendRuleDetails(document, rpm, RoutingPreferenceRuleGroupType.Segments, "Segments", lines);
                    }
                    catch { }
                }
            }

            if (category is "duct" or "all")
            {
                var ductTypes = new FilteredElementCollector(document)
                    .OfClass(typeof(DuctType)).Cast<DuctType>().ToList();

                if (!string.IsNullOrEmpty(typeName))
                    ductTypes = ductTypes.Where(t =>
                        t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase)).ToList();

                lines.Add($"\n--- Duct Types ({ductTypes.Count}) ---");
                lines.Add("| Type | Elbows | Transitions |");
                lines.Add("|------|--------|-------------|");

                foreach (var dt in ductTypes)
                {
                    try
                    {
                        var rpm = dt.RoutingPreferenceManager;
                        int elb = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Elbows);
                        int trn = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Transitions);
                        lines.Add($"| {dt.Name} | {elb} | {trn} |");
                    }
                    catch
                    {
                        lines.Add($"| {dt.Name} | Error | - |");
                    }
                }

                foreach (var dt in ductTypes.Take(5))
                {
                    try
                    {
                        var rpm = dt.RoutingPreferenceManager;
                        lines.Add($"\n  Details for '{dt.Name}':");
                        AppendRuleDetails(document, rpm, RoutingPreferenceRuleGroupType.Elbows, "Elbows", lines);
                        AppendRuleDetails(document, rpm, RoutingPreferenceRuleGroupType.Transitions, "Transitions", lines);
                    }
                    catch { }
                }
            }

            return new
            {
                success = true,
                message = string.Join("\n", lines)
            };
        });

        if (result is null)
            return SkillResult.Fail("Failed to query routing preferences.");

        try
        {
            var dict = result as dynamic;
            return SkillResult.Ok(dict.message.ToString());
        }
        catch
        {
            return SkillResult.Ok(result.ToString() ?? "Done.");
        }
    }

    private static void AppendRuleDetails(
        Document doc,
        RoutingPreferenceManager rpm,
        RoutingPreferenceRuleGroupType group,
        string groupName,
        List<string> lines)
    {
        int count = rpm.GetNumberOfRules(group);
        if (count == 0) return;

        lines.Add($"    {groupName}:");
        for (int i = 0; i < count; i++)
        {
            try
            {
                var rule = rpm.GetRule(group, i);
                var partId = rule.MEPPartId;
                var partElem = doc.GetElement(partId);
                string partName = partElem?.Name ?? $"(ID:{partId.Value})";
                string desc = rule.Description ?? "";
                lines.Add($"      [{i}] {partName} {desc}".TrimEnd());
            }
            catch
            {
                lines.Add($"      [{i}] (error reading rule)");
            }
        }
    }

    private static string GetString(Dictionary<string, object?> p, string key, string def)
    {
        if (!p.TryGetValue(key, out var obj) || obj is null) return def;
        return obj.ToString()?.Trim().ToLowerInvariant() ?? def;
    }
}
