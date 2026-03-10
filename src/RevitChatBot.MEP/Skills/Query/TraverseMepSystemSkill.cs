using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Query;

[Skill("traverse_mep_system",
    "Traverse MEP system graph starting from an element. Uses BFS via connectors (including " +
    "FabricationPart). Returns full system topology: element categories, connector details, " +
    "total length, open ends, max depth, and optionally the connection path.")]
[SkillParameter("element_id", "integer",
    "Starting element ID for graph traversal", isRequired: true)]
[SkillParameter("max_elements", "integer",
    "Maximum elements to visit (default: 500, max: 2000)", isRequired: false)]
[SkillParameter("include_path", "string",
    "Include detailed path with connector info: yes or no. Default: no",
    isRequired: false, allowedValues: ["yes", "no"])]
[SkillParameter("domain_filter", "string",
    "Filter by connector domain: hvac, piping, electrical, all. Default: all",
    isRequired: false, allowedValues: ["hvac", "piping", "electrical", "all"])]
public class TraverseMepSystemSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        if (!TryGetLong(parameters, "element_id", out var startId))
            return SkillResult.Fail("Valid element_id (integer) is required.");

        int maxElems = GetInt(parameters, "max_elements", 500);
        if (maxElems > 2000) maxElems = 2000;
        bool includePath = GetString(parameters, "include_path", "no") == "yes";
        string domainFilter = GetString(parameters, "domain_filter", "all");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var startElem = document.GetElement(new ElementId(startId));
            if (startElem is null)
                return new { error = $"Element {startId} not found." };

            var visited = new HashSet<long>();
            var queue = new Queue<(Element elem, int depth, long? parentId)>();
            queue.Enqueue((startElem, 0, null));
            visited.Add(startElem.Id.Value);

            var catCount = new Dictionary<string, int>();
            var pathLines = new List<string>();
            int openEnds = 0, maxDepth = 0;
            double totalLenFt = 0;
            var connStats = new Dictionary<string, int>();

            while (queue.Count > 0 && visited.Count < maxElems)
            {
                var (elem, depth, parentId) = queue.Dequeue();
                if (depth > maxDepth) maxDepth = depth;

                string cat = elem.Category?.Name ?? "Unknown";
                catCount[cat] = catCount.GetValueOrDefault(cat) + 1;

                if (elem.Location is LocationCurve lc)
                    totalLenFt += lc.Curve.Length;

                string sizeStr = elem.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "";
                string sysName = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";

                if (includePath)
                {
                    string indent = new string(' ', depth * 2);
                    string arrow = parentId.HasValue ? $" ← {parentId}" : " (START)";
                    pathLines.Add($"{indent}[{depth}] ID:{elem.Id} {cat} {sizeStr} sys={sysName}{arrow}");
                }

                ConnectorManager cm = GetConnectorManager(elem);
                if (cm == null) continue;

                foreach (Connector c in cm.Connectors)
                {
                    if (domainFilter != "all")
                    {
                        bool match = domainFilter switch
                        {
                            "hvac" => c.Domain == Domain.DomainHvac,
                            "piping" => c.Domain == Domain.DomainPiping,
                            "electrical" => c.Domain == Domain.DomainElectrical,
                            _ => true
                        };
                        if (!match) continue;
                    }

                    string domainKey = c.Domain.ToString();
                    connStats[domainKey] = connStats.GetValueOrDefault(domainKey) + 1;

                    if (!c.IsConnected)
                    {
                        openEnds++;
                        if (includePath)
                        {
                            string indent = new string(' ', (depth + 1) * 2);
                            pathLines.Add($"{indent}[OPEN] {c.Domain} {c.Shape}");
                        }
                        continue;
                    }

                    foreach (Connector other in c.AllRefs)
                    {
                        if (other?.Owner == null) continue;
                        if (other.ConnectorType == ConnectorType.Logical) continue;
                        if (visited.Add(other.Owner.Id.Value))
                            queue.Enqueue((other.Owner, depth + 1, elem.Id.Value));
                    }
                }
            }

            var totalLenM = Math.Round(totalLenFt * 0.3048, 1);
            var summary = new List<string>
            {
                $"MEP System Graph from ID:{startId}",
                $"  Total elements: {visited.Count}" +
                    (visited.Count >= maxElems ? $" (limit {maxElems} reached)" : ""),
                $"  Max depth: {maxDepth}",
                $"  Open ends: {openEnds}",
                $"  Total curve length: {totalLenM}m",
                "",
                "  By category:"
            };
            foreach (var kv in catCount.OrderByDescending(x => x.Value))
                summary.Add($"    {kv.Key}: {kv.Value}");

            if (connStats.Count > 0)
            {
                summary.Add("");
                summary.Add("  Connector domains:");
                foreach (var kv in connStats.OrderByDescending(x => x.Value))
                    summary.Add($"    {kv.Key}: {kv.Value}");
            }

            if (includePath && pathLines.Count > 0)
            {
                summary.Add("");
                summary.Add("  Traversal path:");
                int limit = Math.Min(pathLines.Count, 100);
                summary.AddRange(pathLines.Take(limit));
                if (pathLines.Count > limit)
                    summary.Add($"  ... and {pathLines.Count - limit} more nodes");
            }

            return new
            {
                success = true,
                message = string.Join("\n", summary),
                elementCount = visited.Count,
                maxDepth,
                openEnds,
                totalLengthM = totalLenM,
                categories = catCount
            };
        });

        if (result is null)
            return SkillResult.Fail("Failed to traverse MEP system.");

        var dict = result as dynamic;
        try
        {
            if (dict.error != null)
                return SkillResult.Fail(dict.error.ToString());
        }
        catch { }

        try
        {
            return SkillResult.Ok(dict.message.ToString());
        }
        catch
        {
            return SkillResult.Ok(result.ToString() ?? "Traversal complete.");
        }
    }

    private static ConnectorManager? GetConnectorManager(Element elem)
    {
        if (elem is MEPCurve mc) return mc.ConnectorManager;
        if (elem is FamilyInstance fi) return fi.MEPModel?.ConnectorManager;
        try
        {
            var prop = elem.GetType().GetProperty("ConnectorManager");
            return prop?.GetValue(elem) as ConnectorManager;
        }
        catch { return null; }
    }

    private static bool TryGetLong(Dictionary<string, object?> p, string key, out long val)
    {
        val = 0;
        if (!p.TryGetValue(key, out var obj) || obj is null) return false;
        if (obj is long l) { val = l; return true; }
        if (obj is int i) { val = i; return true; }
        if (obj is double d) { val = (long)d; return true; }
        return long.TryParse(obj.ToString(), out val);
    }

    private static int GetInt(Dictionary<string, object?> p, string key, int def)
    {
        if (!p.TryGetValue(key, out var obj) || obj is null) return def;
        if (obj is int i) return i;
        if (obj is long l) return (int)l;
        if (obj is double d) return (int)d;
        return int.TryParse(obj.ToString(), out var v) ? v : def;
    }

    private static string GetString(Dictionary<string, object?> p, string key, string def)
    {
        if (!p.TryGetValue(key, out var obj) || obj is null) return def;
        return obj.ToString()?.Trim().ToLowerInvariant() ?? def;
    }
}
