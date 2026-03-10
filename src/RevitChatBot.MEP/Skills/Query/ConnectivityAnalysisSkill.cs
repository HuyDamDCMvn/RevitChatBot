using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Query;

[Skill("connectivity_analysis", "Analyze MEP connectivity from a starting element using BFS traversal. Returns network elements, depths, and open endpoint count.")]
[SkillParameter("elementId", "integer", "Starting element ID for connectivity traversal", isRequired: true)]
[SkillParameter("maxDepth", "integer", "Maximum BFS depth (default: 20)", isRequired: false)]
public class ConnectivityAnalysisSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        if (!TryGetLong(parameters, "elementId", out var startId))
            return SkillResult.Fail("Valid elementId (integer) is required.");

        var maxDepth = 20;
        if (parameters.TryGetValue("maxDepth", out var md) && md != null)
        {
            if (md is int i) maxDepth = i;
            else if (md is long l) maxDepth = (int)l;
            else int.TryParse(md.ToString(), out maxDepth);
        }

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var startElement = document.GetElement(new ElementId(startId));
            if (startElement is null)
                return new { error = "Element not found" };

            var visited = new HashSet<long>();
            var queue = new Queue<(Element elem, int depth)>();
            queue.Enqueue((startElement, 0));
            visited.Add(startElement.Id.Value);
            var network = new List<object>();
            var openEndCount = 0;

            while (queue.Count > 0)
            {
                var (elem, depth) = queue.Dequeue();
                if (depth > maxDepth) continue;

                var categoryName = elem.Category?.Name ?? "Unknown";
                var typeName = elem.GetType().Name;
                if (elem is FamilyInstance fi && fi.Symbol != null)
                    typeName = fi.Symbol.Name;

                network.Add(new
                {
                    element_id = elem.Id.Value,
                    category_name = categoryName,
                    type_name = typeName,
                    depth
                });

                var connectorManager = GetConnectorManager(elem);
                if (connectorManager is null) continue;

                foreach (Connector conn in connectorManager.Connectors)
                {
                    if (!conn.IsConnected)
                        openEndCount++;

                    foreach (Connector refConn in conn.AllRefs)
                    {
                        if (refConn.Owner is not Element owner) continue;
                        var ownerId = owner.Id.Value;
                        if (visited.Add(ownerId) && depth + 1 <= maxDepth)
                            queue.Enqueue((owner, depth + 1));
                    }
                }
            }

            return new
            {
                traversed_count = network.Count,
                network,
                open_end_count = openEndCount
            };
        });

        var data = result as dynamic;
        if (data?.error != null)
            return SkillResult.Fail(data.error.ToString(), null);

        return SkillResult.Ok("Connectivity analysis completed.", result);
    }

    private static ConnectorManager? GetConnectorManager(Element elem)
    {
        if (elem is MEPCurve curve)
            return curve.ConnectorManager;
        if (elem is FamilyInstance fi && fi.MEPModel != null)
            return fi.MEPModel.ConnectorManager;
        return null;
    }

    private static bool TryGetLong(Dictionary<string, object?> p, string key, out long value)
    {
        value = 0;
        if (!p.TryGetValue(key, out var v) || v is null) return false;
        if (v is long l) { value = l; return true; }
        if (v is int i) { value = i; return true; }
        return long.TryParse(v.ToString(), out value);
    }
}
