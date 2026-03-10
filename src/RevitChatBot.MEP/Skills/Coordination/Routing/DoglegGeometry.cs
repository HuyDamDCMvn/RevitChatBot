using Autodesk.Revit.DB;

namespace RevitChatBot.MEP.Skills.Coordination.Routing;

/// <summary>
/// Computes the 6 waypoints for a dogleg reroute around an obstruction.
/// 
/// The dogleg has 5 segments:
///   P0 ── P1 (approach in original direction)
///         │ P2 (offset leg: move away from obstacle)
///         │ P3 (travel leg: pass the obstacle)
///         │ P4 (return leg: come back to original alignment)
///   P4 ── P5 (departure in original direction)
/// </summary>
public static class DoglegGeometry
{
    /// <param name="startPt">Original start point of the element</param>
    /// <param name="endPt">Original end point of the element</param>
    /// <param name="obstacleBounds">Union bounding box of stand elements</param>
    /// <param name="offsetFeet">Offset distance from obstacle (clearance)</param>
    /// <param name="direction">Direction to offset away from obstacle</param>
    /// <returns>6 points (P0..P5) defining the dogleg, or null if geometry is degenerate</returns>
    public static XYZ[]? ComputeDoglegPoints(
        XYZ startPt,
        XYZ endPt,
        BoundingBoxXYZ obstacleBounds,
        double offsetFeet,
        RouteDirection direction)
    {
        var lineDir = (endPt - startPt);
        double lineLength = lineDir.GetLength();
        if (lineLength < 1e-9) return null;
        lineDir = lineDir.Normalize();

        var offsetVector = DirectionClassifier.GetDirectionVector(direction);

        double approachDist = ComputeApproachDistance(startPt, lineDir, obstacleBounds);
        double departureDist = ComputeDepartureDistance(startPt, lineDir, lineLength, obstacleBounds);

        double cushion = offsetFeet * 0.5;
        approachDist = Math.Max(0, approachDist - cushion);
        departureDist = Math.Min(lineLength, departureDist + cushion);

        if (approachDist >= departureDist || approachDist < 0 || departureDist > lineLength)
            return null;

        var obstacleExtent = ComputeRequiredOffset(startPt, obstacleBounds, offsetVector);
        double totalOffset = obstacleExtent + offsetFeet;

        var p0 = startPt;
        var p1 = startPt + lineDir * approachDist;
        var p2 = p1 + offsetVector * totalOffset;
        var p3 = startPt + lineDir * departureDist + offsetVector * totalOffset;
        var p4 = startPt + lineDir * departureDist;
        var p5 = endPt;

        return [p0, p1, p2, p3, p4, p5];
    }

    /// <summary>
    /// Simplified variant: compute offset without target bounds, 
    /// just push by a fixed amount in the given direction.
    /// </summary>
    public static XYZ[]? ComputeSimpleDoglegPoints(
        XYZ startPt,
        XYZ endPt,
        double splitRatioStart,
        double splitRatioEnd,
        double offsetFeet,
        RouteDirection direction)
    {
        var lineDir = (endPt - startPt);
        double lineLength = lineDir.GetLength();
        if (lineLength < 1e-9) return null;
        lineDir = lineDir.Normalize();

        var offsetVector = DirectionClassifier.GetDirectionVector(direction);

        double approachDist = lineLength * splitRatioStart;
        double departureDist = lineLength * splitRatioEnd;

        var p0 = startPt;
        var p1 = startPt + lineDir * approachDist;
        var p2 = p1 + offsetVector * offsetFeet;
        var p3 = startPt + lineDir * departureDist + offsetVector * offsetFeet;
        var p4 = startPt + lineDir * departureDist;
        var p5 = endPt;

        return [p0, p1, p2, p3, p4, p5];
    }

    private static double ComputeApproachDistance(XYZ origin, XYZ lineDir, BoundingBoxXYZ bb)
    {
        var corners = GetBoxCorners(bb);
        double minProj = double.MaxValue;
        foreach (var c in corners)
        {
            double proj = (c - origin).DotProduct(lineDir);
            minProj = Math.Min(minProj, proj);
        }
        return Math.Max(0, minProj);
    }

    private static double ComputeDepartureDistance(XYZ origin, XYZ lineDir, double lineLength, BoundingBoxXYZ bb)
    {
        var corners = GetBoxCorners(bb);
        double maxProj = double.MinValue;
        foreach (var c in corners)
        {
            double proj = (c - origin).DotProduct(lineDir);
            maxProj = Math.Max(maxProj, proj);
        }
        return Math.Min(lineLength, maxProj);
    }

    private static double ComputeRequiredOffset(XYZ origin, BoundingBoxXYZ bb, XYZ offsetDir)
    {
        var corners = GetBoxCorners(bb);
        double maxProj = 0;
        foreach (var c in corners)
        {
            double proj = (c - origin).DotProduct(offsetDir);
            maxProj = Math.Max(maxProj, proj);
        }
        return Math.Max(0, maxProj);
    }

    private static XYZ[] GetBoxCorners(BoundingBoxXYZ bb)
    {
        var min = bb.Min;
        var max = bb.Max;
        return
        [
            new XYZ(min.X, min.Y, min.Z),
            new XYZ(max.X, min.Y, min.Z),
            new XYZ(min.X, max.Y, min.Z),
            new XYZ(max.X, max.Y, min.Z),
            new XYZ(min.X, min.Y, max.Z),
            new XYZ(max.X, min.Y, max.Z),
            new XYZ(min.X, max.Y, max.Z),
            new XYZ(max.X, max.Y, max.Z),
        ];
    }
}
