using Autodesk.Revit.DB;

namespace RevitChatBot.MEP.Skills.Coordination.Routing;

/// <summary>
/// Groups clashing pairs into connected components using BFS.
/// Each group = a cluster of shift elements sharing transitivity through stand elements.
/// </summary>
public static class ConnectedComponentAnalyzer
{
    public static List<ClashGroup> BuildClashGroups(
        List<(Element Shift, Element Stand)> clashPairs)
    {
        if (clashPairs.Count == 0)
            return [];

        var adjacency = new Dictionary<long, HashSet<long>>();
        var elementById = new Dictionary<long, Element>();
        var isShift = new HashSet<long>();
        var isStand = new HashSet<long>();

        foreach (var (shift, stand) in clashPairs)
        {
            long sId = shift.Id.Value;
            long tId = stand.Id.Value;

            isShift.Add(sId);
            isStand.Add(tId);
            elementById.TryAdd(sId, shift);
            elementById.TryAdd(tId, stand);

            if (!adjacency.ContainsKey(sId))
                adjacency[sId] = [];
            if (!adjacency.ContainsKey(tId))
                adjacency[tId] = [];

            adjacency[sId].Add(tId);
            adjacency[tId].Add(sId);
        }

        var visited = new HashSet<long>();
        var groups = new List<ClashGroup>();

        foreach (var startId in adjacency.Keys)
        {
            if (visited.Contains(startId)) continue;

            var queue = new Queue<long>();
            queue.Enqueue(startId);
            visited.Add(startId);

            var groupShift = new List<Element>();
            var groupStand = new List<Element>();

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                if (isShift.Contains(current))
                    groupShift.Add(elementById[current]);
                if (isStand.Contains(current))
                    groupStand.Add(elementById[current]);

                foreach (var neighbor in adjacency[current])
                {
                    if (visited.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            if (groupShift.Count > 0 && groupStand.Count > 0)
            {
                var group = new ClashGroup
                {
                    ShiftElements = groupShift,
                    StandElements = groupStand,
                    UnionStandBounds = ComputeUnionBounds(groupStand)
                };
                groups.Add(group);
            }
        }

        return groups;
    }

    private static BoundingBoxXYZ? ComputeUnionBounds(List<Element> elements)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        bool hasAny = false;

        foreach (var elem in elements)
        {
            var bb = elem.get_BoundingBox(null);
            if (bb is null) continue;
            hasAny = true;

            minX = Math.Min(minX, bb.Min.X);
            minY = Math.Min(minY, bb.Min.Y);
            minZ = Math.Min(minZ, bb.Min.Z);
            maxX = Math.Max(maxX, bb.Max.X);
            maxY = Math.Max(maxY, bb.Max.Y);
            maxZ = Math.Max(maxZ, bb.Max.Z);
        }

        if (!hasAny) return null;

        return new BoundingBoxXYZ
        {
            Min = new XYZ(minX, minY, minZ),
            Max = new XYZ(maxX, maxY, maxZ)
        };
    }
}
