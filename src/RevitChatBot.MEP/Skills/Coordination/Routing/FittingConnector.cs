using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace RevitChatBot.MEP.Skills.Coordination.Routing;

/// <summary>
/// Connects adjacent MEP segments with fittings (elbows) at junction points.
/// Uses Revit API utilities for each discipline.
/// </summary>
public static class FittingConnector
{
    /// <returns>True if connected or fitting placed successfully</returns>
    public static bool ConnectWithFitting(Document doc, MEPCurve segA, MEPCurve segB, XYZ junctionPt)
    {
        var connA = FindNearestUnconnectedConnector(segA, junctionPt);
        var connB = FindNearestUnconnectedConnector(segB, junctionPt);

        if (connA is null || connB is null)
            return false;

        if (AreAligned(connA, connB))
        {
            try
            {
                connA.ConnectTo(connB);
                return true;
            }
            catch { /* fall through to fitting creation */ }
        }

        return TryCreateElbow(doc, connA, connB);
    }

    /// <summary>
    /// Attempt to connect all segments in order using fittings.
    /// </summary>
    /// <returns>Number of successful connections</returns>
    public static int ConnectSegmentChain(Document doc, IReadOnlyList<MEPCurve> segments, IReadOnlyList<XYZ> junctions)
    {
        int success = 0;
        int count = Math.Min(segments.Count - 1, junctions.Count);

        for (int i = 0; i < count; i++)
        {
            if (ConnectWithFitting(doc, segments[i], segments[i + 1], junctions[i]))
                success++;
        }

        return success;
    }

    private static bool TryCreateElbow(Document doc, Connector connA, Connector connB)
    {
        try
        {
            var owner = connA.Owner;
            if (owner is Pipe or FlexPipe)
            {
                PlumbingUtils.ConnectPipePlaceholdersAtElbow(doc, connA, connB);
                return true;
            }
        }
        catch { /* try next method */ }

        try
        {
            var owner = connA.Owner;
            if (owner is Duct or FlexDuct)
            {
                MechanicalUtils.ConnectDuctPlaceholdersAtElbow(doc, connA, connB);
                return true;
            }
        }
        catch { /* try next method */ }

        try
        {
            var owner = connA.Owner;
            if (owner is CableTray or Conduit)
            {
                // For cable tray and conduit, try direct connection
                connA.ConnectTo(connB);
                return true;
            }
        }
        catch { /* connection failed */ }

        try
        {
            doc.Create.NewElbowFitting(connA, connB);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Connector? FindNearestUnconnectedConnector(MEPCurve curve, XYZ point)
    {
        var cm = curve.ConnectorManager;
        if (cm is null) return null;

        Connector? nearest = null;
        double minDist = double.MaxValue;

        foreach (Connector c in cm.Connectors)
        {
            if (c.IsConnected) continue;
            double dist = c.Origin.DistanceTo(point);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = c;
            }
        }

        // Fallback: if all connected, pick the nearest anyway for fitting insertion
        if (nearest is null)
        {
            foreach (Connector c in cm.Connectors)
            {
                double dist = c.Origin.DistanceTo(point);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = c;
                }
            }
        }

        return nearest;
    }

    private static bool AreAligned(Connector a, Connector b)
    {
        if (a.Origin.DistanceTo(b.Origin) > 0.01) return false;
        double dot = Math.Abs(a.CoordinateSystem.BasisZ.DotProduct(b.CoordinateSystem.BasisZ));
        return dot > 0.99;
    }
}
