using Autodesk.Revit.DB;

namespace RevitChatBot.MEP.Skills.Coordination.Routing;

public enum RouteRelation
{
    Parallel,
    Perpendicular
}

public enum RouteDirection
{
    Up,
    Down,
    Left,
    Right
}

/// <summary>
/// Classifies the reroute direction for a shift element relative to a stand element.
/// Uses XY-projected dot product to determine parallel/perpendicular relation,
/// with Z-span fallback for vertical risers.
/// </summary>
public static class DirectionClassifier
{
    private const double ParallelThreshold = 0.98;
    private const double PerpendicularThreshold = 0.10;

    public static RouteRelation ClassifyRelation(Element shiftElem, Element standElem)
    {
        var shiftDir = GetElementDirection(shiftElem);
        var standDir = GetElementDirection(standElem);

        if (shiftDir is null || standDir is null)
        {
            var bb = standElem.get_BoundingBox(null);
            if (bb is not null)
            {
                bool isRiser = (bb.Max.Z - bb.Min.Z) > 2.0;
                return isRiser ? RouteRelation.Perpendicular : RouteRelation.Parallel;
            }
            return RouteRelation.Perpendicular;
        }

        var xyShift = new XYZ(shiftDir.X, shiftDir.Y, 0).Normalize();
        var xyStand = new XYZ(standDir.X, standDir.Y, 0).Normalize();
        double dot = Math.Abs(xyShift.DotProduct(xyStand));

        return dot >= ParallelThreshold ? RouteRelation.Parallel : RouteRelation.Perpendicular;
    }

    public static RouteDirection ChooseDirection(
        RouteRelation relation,
        RouteDirection parallelDefault = RouteDirection.Down,
        RouteDirection perpendicularDefault = RouteDirection.Right)
    {
        return relation == RouteRelation.Parallel ? parallelDefault : perpendicularDefault;
    }

    public static XYZ GetDirectionVector(RouteDirection direction) => direction switch
    {
        RouteDirection.Up => XYZ.BasisZ,
        RouteDirection.Down => -XYZ.BasisZ,
        RouteDirection.Left => -XYZ.BasisY,
        RouteDirection.Right => XYZ.BasisY,
        _ => -XYZ.BasisZ
    };

    private static XYZ? GetElementDirection(Element elem)
    {
        if (elem.Location is LocationCurve locCurve)
        {
            var curve = locCurve.Curve;
            var start = curve.GetEndPoint(0);
            var end = curve.GetEndPoint(1);
            var dir = (end - start);
            return dir.GetLength() > 1e-9 ? dir.Normalize() : null;
        }
        return null;
    }
}
