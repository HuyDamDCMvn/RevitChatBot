using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.HVAC;

/// <summary>
/// Computes a tree-topology duct layout for air terminals (diffusers/grilles).
///
/// Strategy (mirrors SprinklerRoutingAlgorithm):
///   1. Collect terminal XYZ positions and airflow per terminal
///   2. Determine principal axis via bounding-box aspect ratio
///   3. Cluster terminals into rows perpendicular to principal axis
///   4. Route branch ducts through each row
///   5. Route a main duct connecting all branch ducts
///   6. Identify fitting (tee/elbow) positions at junctions
///
/// Duct sizing uses ASHRAE equal-velocity method:
///   - Branch ducts: sized for cumulative flow in each row
///   - Main duct: sized for total flow from all branches
///   - Velocity limits: branch 3–5 m/s, main 5–8 m/s
/// </summary>
public static class DuctTerminalRoutingAlgorithm
{
    private static readonly double[] StandardRoundMm =
    {
        100, 125, 150, 200, 250, 300, 350, 400, 450, 500,
        550, 600, 650, 700, 750, 800, 900, 1000, 1100, 1200
    };

    private static readonly int[] StandardRectMm =
    {
        100, 125, 150, 200, 250, 300, 350, 400, 450, 500,
        550, 600, 650, 700, 750, 800, 900, 1000, 1100, 1200,
        1400, 1500, 1600, 1800, 2000
    };

    public static MepAutoRouteData ComputeRoute(
        IReadOnlyList<(XYZ Position, double FlowLps)> terminals,
        double branchVelocityMps = 4.0,
        double mainVelocityMps = 7.0,
        double maxAspectRatio = 3.0,
        bool preferRectangular = false,
        string? levelName = null)
    {
        var data = new MepAutoRouteData
        {
            Domain = "hvac",
            ElementCategory = "air_terminal",
            TotalEndpoints = terminals.Count,
            LevelName = levelName
        };

        if (terminals.Count == 0)
            return data;

        var positions = terminals.Select(t => t.Position).ToList();
        var flowMap = terminals.ToDictionary(t => t.Position, t => t.FlowLps);

        foreach (var t in terminals)
            data.EndpointPositions.Add(new PointData(t.Position.X, t.Position.Y, t.Position.Z));

        data.TotalFlowLps = terminals.Sum(t => t.FlowLps);

        if (terminals.Count == 1)
        {
            var size = SizeDuct(data.TotalFlowLps, branchVelocityMps, maxAspectRatio, preferRectangular);
            data.BranchSizeMm = size.Diameter;
            data.MainSizeMm = size.Diameter;
            if (size.Width.HasValue) { data.BranchWidthMm = size.Width; data.BranchHeightMm = size.Height; }
            return data;
        }

        var (principalAxis, rows) = ClusterIntoRows(positions);
        double referenceZ = positions.Average(p => p.Z);
        var branchMidpoints = new List<XYZ>();

        foreach (var row in rows)
        {
            if (row.Count == 0) continue;

            var sorted = principalAxis == PrincipalAxis.X
                ? row.OrderBy(p => p.X).ToList()
                : row.OrderBy(p => p.Y).ToList();

            double rowFlowLps = sorted.Sum(p => flowMap.GetValueOrDefault(p, 0));
            var branchSize = SizeDuct(rowFlowLps, branchVelocityMps, maxAspectRatio, preferRectangular);

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                data.BranchSegments.Add(new SegmentData
                {
                    Start = new PointData(sorted[i].X, sorted[i].Y, sorted[i].Z),
                    End = new PointData(sorted[i + 1].X, sorted[i + 1].Y, sorted[i + 1].Z),
                    DiameterMm = branchSize.Diameter
                });
            }

            var midX = sorted.Average(p => p.X);
            var midY = sorted.Average(p => p.Y);
            branchMidpoints.Add(new XYZ(midX, midY, referenceZ));

            data.BranchSizeMm = branchSize.Diameter;
            if (branchSize.Width.HasValue)
            {
                data.BranchWidthMm = branchSize.Width;
                data.BranchHeightMm = branchSize.Height;
            }
        }

        if (branchMidpoints.Count >= 2)
        {
            var sortedMids = principalAxis == PrincipalAxis.X
                ? branchMidpoints.OrderBy(p => p.Y).ToList()
                : branchMidpoints.OrderBy(p => p.X).ToList();

            var mainSize = SizeDuct(data.TotalFlowLps, mainVelocityMps, maxAspectRatio, preferRectangular);

            for (int i = 0; i < sortedMids.Count - 1; i++)
            {
                data.MainSegments.Add(new SegmentData
                {
                    Start = new PointData(sortedMids[i].X, sortedMids[i].Y, sortedMids[i].Z),
                    End = new PointData(sortedMids[i + 1].X, sortedMids[i + 1].Y, sortedMids[i + 1].Z),
                    DiameterMm = mainSize.Diameter
                });
            }

            data.MainSizeMm = mainSize.Diameter;
            if (mainSize.Width.HasValue)
            {
                data.MainWidthMm = mainSize.Width;
                data.MainHeightMm = mainSize.Height;
            }

            AddBranchToMainConnections(data, rows, sortedMids, principalAxis, referenceZ);
        }
        else if (branchMidpoints.Count == 1)
        {
            var mainSize = SizeDuct(data.TotalFlowLps, mainVelocityMps, maxAspectRatio, preferRectangular);
            data.MainSizeMm = mainSize.Diameter;
            if (mainSize.Width.HasValue) { data.MainWidthMm = mainSize.Width; data.MainHeightMm = mainSize.Height; }
        }

        data.TotalSegments = data.BranchSegments.Count + data.MainSegments.Count;
        data.TotalFittings = data.FittingPositions.Count;
        data.TotalLengthFeet = data.BranchSegments.Sum(s => s.LengthFeet)
                             + data.MainSegments.Sum(s => s.LengthFeet);

        return data;
    }

    private static void AddBranchToMainConnections(
        MepAutoRouteData data,
        List<List<XYZ>> rows,
        List<XYZ> sortedMids,
        PrincipalAxis principalAxis,
        double referenceZ)
    {
        foreach (var row in rows)
        {
            if (row.Count == 0) continue;

            var sorted = principalAxis == PrincipalAxis.X
                ? row.OrderBy(p => p.X).ToList()
                : row.OrderBy(p => p.Y).ToList();

            var rowMidX = sorted.Average(p => p.X);
            var rowMidY = sorted.Average(p => p.Y);
            var closestMain = sortedMids
                .OrderBy(m => Math.Pow(m.X - rowMidX, 2) + Math.Pow(m.Y - rowMidY, 2))
                .First();

            var branchEnd = principalAxis == PrincipalAxis.X
                ? new XYZ(closestMain.X, sorted[0].Y, referenceZ)
                : new XYZ(sorted[0].X, closestMain.Y, referenceZ);

            if (branchEnd.DistanceTo(closestMain) > 0.05)
            {
                data.BranchSegments.Add(new SegmentData
                {
                    Start = new PointData(branchEnd.X, branchEnd.Y, branchEnd.Z),
                    End = new PointData(closestMain.X, closestMain.Y, closestMain.Z),
                    DiameterMm = data.BranchSizeMm
                });
            }

            data.FittingPositions.Add(new PointData(closestMain.X, closestMain.Y, referenceZ));
        }

        foreach (var mid in sortedMids)
            data.FittingPositions.Add(new PointData(mid.X, mid.Y, mid.Z));
    }

    #region Spatial Clustering

    private static (PrincipalAxis axis, List<List<XYZ>> rows) ClusterIntoRows(
        IReadOnlyList<XYZ> positions)
    {
        double minX = positions.Min(p => p.X), maxX = positions.Max(p => p.X);
        double minY = positions.Min(p => p.Y), maxY = positions.Max(p => p.Y);
        double spanX = maxX - minX;
        double spanY = maxY - minY;

        PrincipalAxis axis;
        double clusterTolerance;

        if (spanX >= spanY)
        {
            axis = PrincipalAxis.X;
            clusterTolerance = spanY > 0 ? spanY / Math.Max(positions.Count / 4.0, 2) : 1.0;
        }
        else
        {
            axis = PrincipalAxis.Y;
            clusterTolerance = spanX > 0 ? spanX / Math.Max(positions.Count / 4.0, 2) : 1.0;
        }

        clusterTolerance = Math.Max(clusterTolerance, 0.5);

        var sorted = axis == PrincipalAxis.X
            ? positions.OrderBy(p => p.Y).ToList()
            : positions.OrderBy(p => p.X).ToList();

        var rows = new List<List<XYZ>>();
        var currentRow = new List<XYZ> { sorted[0] };

        for (int i = 1; i < sorted.Count; i++)
        {
            double coord = axis == PrincipalAxis.X ? sorted[i].Y : sorted[i].X;
            double prevCoord = axis == PrincipalAxis.X ? sorted[i - 1].Y : sorted[i - 1].X;

            if (Math.Abs(coord - prevCoord) > clusterTolerance)
            {
                rows.Add(currentRow);
                currentRow = [];
            }
            currentRow.Add(sorted[i]);
        }
        if (currentRow.Count > 0)
            rows.Add(currentRow);

        return (axis, rows);
    }

    private enum PrincipalAxis { X, Y }

    #endregion

    #region Duct Sizing — ASHRAE Velocity Method

    private record DuctSize(double Diameter, double? Width, double? Height);

    /// <summary>
    /// Size duct from flow and velocity using A = Q / V.
    /// Returns equivalent round diameter and optional rectangular W×H.
    /// </summary>
    private static DuctSize SizeDuct(
        double flowLps, double velocityMps,
        double maxAspectRatio, bool preferRectangular)
    {
        if (flowLps <= 0)
            return new DuctSize(StandardRoundMm[0], null, null);

        double flowM3s = flowLps / 1000.0;
        double areaSqM = flowM3s / velocityMps;
        double diameterMm = Math.Sqrt(4.0 * areaSqM / Math.PI) * 1000.0;
        double roundDn = SnapToStandardRound(diameterMm);

        if (!preferRectangular)
            return new DuctSize(roundDn, null, null);

        double areaMm2 = areaSqM * 1_000_000;
        double widthCalc = Math.Sqrt(areaMm2 * maxAspectRatio);
        double heightCalc = areaMm2 / widthCalc;

        int w = SnapToStandardRect(widthCalc);
        int h = SnapToStandardRect(heightCalc);
        if (w < h) (w, h) = (h, w);

        // Equivalent diameter for rectangular: De = 1.3 * (W*H)^0.625 / (W+H)^0.25
        double de = 1.3 * Math.Pow((double)w * h, 0.625) / Math.Pow(w + h, 0.25);

        return new DuctSize(de, w, h);
    }

    private static double SnapToStandardRound(double diameterMm)
    {
        foreach (var d in StandardRoundMm)
            if (d >= diameterMm) return d;
        return StandardRoundMm[^1];
    }

    private static int SnapToStandardRect(double valueMm)
    {
        if (valueMm <= 0) return StandardRectMm[0];
        foreach (var s in StandardRectMm)
            if (s >= valueMm) return s;
        return StandardRectMm[^1];
    }

    #endregion
}
