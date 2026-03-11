using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Sprinkler;

/// <summary>
/// Computes a tree-topology piping layout for sprinkler heads.
///
/// Strategy:
///   1. Collect sprinkler XYZ positions
///   2. Determine principal axis via bounding-box aspect ratio
///   3. Cluster heads into rows perpendicular to principal axis
///   4. Route branch lines through each row
///   5. Route a cross main connecting all branch lines
///   6. Identify fitting (tee/elbow) positions at junctions
///
/// Pipe diameters are assigned by NFPA 13 rules:
///   - Branch lines: sized for the number of heads downstream
///   - Cross main: sized for total flow from all branches
/// </summary>
public static class SprinklerRoutingAlgorithm
{
    private static readonly Dictionary<string, HazardCriteria> HazardTable = new()
    {
        ["light"] = new(21, 4.6, 139, 4.1),
        ["oh1"] = new(12, 4.6, 139, 6.1),
        ["oh2"] = new(12, 4.6, 139, 8.2),
        ["extra"] = new(9, 3.7, 232, 12.2)
    };

    private static readonly double[] StandardDnMm =
    {
        25, 32, 40, 50, 65, 80, 100, 125, 150, 200
    };

    public static SprinklerRouteData ComputeRoute(
        IReadOnlyList<XYZ> headPositions,
        string hazardClass = "light",
        double kFactor = 80,
        double minPressureBar = 0.5,
        string? levelName = null)
    {
        var data = new SprinklerRouteData
        {
            HazardClass = hazardClass,
            TotalHeads = headPositions.Count,
            LevelName = levelName
        };

        if (headPositions.Count == 0)
            return data;

        if (!HazardTable.TryGetValue(hazardClass, out var criteria))
            criteria = HazardTable["light"];

        data.CoverageRadiusFeet = Math.Sqrt(criteria.CoveragePerHeadM2 / Math.PI) / 0.3048;

        foreach (var pos in headPositions)
            data.HeadPositions.Add(new PointData(pos.X, pos.Y, pos.Z));

        if (headPositions.Count == 1)
        {
            data.BranchDiameterMm = 25;
            data.MainDiameterMm = 25;
            return data;
        }

        var (principalAxis, rows) = ClusterIntoRows(headPositions);

        double referenceZ = headPositions.Average(p => p.Z);

        var branchMidpoints = new List<XYZ>();

        foreach (var row in rows)
        {
            if (row.Count == 0) continue;

            var sorted = principalAxis == PrincipalAxis.X
                ? row.OrderBy(p => p.X).ToList()
                : row.OrderBy(p => p.Y).ToList();

            double branchDn = SizeBranchPipe(sorted.Count, kFactor, minPressureBar, criteria);

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var start = sorted[i];
                var end = sorted[i + 1];
                data.BranchSegments.Add(new SegmentData
                {
                    Start = new PointData(start.X, start.Y, start.Z),
                    End = new PointData(end.X, end.Y, end.Z),
                    DiameterMm = branchDn
                });
            }

            var midX = sorted.Average(p => p.X);
            var midY = sorted.Average(p => p.Y);
            branchMidpoints.Add(new XYZ(midX, midY, referenceZ));

            data.BranchDiameterMm = branchDn;
        }

        if (branchMidpoints.Count >= 2)
        {
            var sortedMids = principalAxis == PrincipalAxis.X
                ? branchMidpoints.OrderBy(p => p.Y).ToList()
                : branchMidpoints.OrderBy(p => p.X).ToList();

            double mainDn = SizeMainPipe(headPositions.Count, kFactor, minPressureBar);

            for (int i = 0; i < sortedMids.Count - 1; i++)
            {
                var start = sortedMids[i];
                var end = sortedMids[i + 1];
                data.MainSegments.Add(new SegmentData
                {
                    Start = new PointData(start.X, start.Y, start.Z),
                    End = new PointData(end.X, end.Y, end.Z),
                    DiameterMm = mainDn
                });
            }

            data.MainDiameterMm = mainDn;

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

                bool alreadyOnMain = branchEnd.DistanceTo(closestMain) < 0.01;
                if (!alreadyOnMain && branchEnd.DistanceTo(closestMain) > 0.05)
                {
                    data.BranchSegments.Add(new SegmentData
                    {
                        Start = new PointData(branchEnd.X, branchEnd.Y, branchEnd.Z),
                        End = new PointData(closestMain.X, closestMain.Y, closestMain.Z),
                        DiameterMm = data.BranchDiameterMm
                    });
                }

                data.FittingPositions.Add(new PointData(closestMain.X, closestMain.Y, referenceZ));
            }

            foreach (var mid in sortedMids)
                data.FittingPositions.Add(new PointData(mid.X, mid.Y, mid.Z));
        }
        else if (branchMidpoints.Count == 1)
        {
            data.MainDiameterMm = SizeMainPipe(headPositions.Count, kFactor, minPressureBar);
        }

        data.TotalSegments = data.BranchSegments.Count + data.MainSegments.Count;
        data.TotalFittings = data.FittingPositions.Count;
        data.TotalLengthFeet = data.BranchSegments.Sum(s => s.LengthFeet) +
                               data.MainSegments.Sum(s => s.LengthFeet);

        return data;
    }

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

    private static double SizeBranchPipe(int headCount, double kFactor, double minPressure, HazardCriteria criteria)
    {
        double flowPerHead = kFactor * Math.Sqrt(minPressure) / 60.0;
        double totalFlowLps = flowPerHead * headCount;
        double velocityMps = 3.0;
        double areaSqm = totalFlowLps / 1000.0 / velocityMps;
        double diameterMm = Math.Sqrt(4 * areaSqm / Math.PI) * 1000;
        return SnapToStandardDn(diameterMm);
    }

    private static double SizeMainPipe(int totalHeadCount, double kFactor, double minPressure)
    {
        double flowPerHead = kFactor * Math.Sqrt(minPressure) / 60.0;
        double totalFlowLps = flowPerHead * totalHeadCount;
        double velocityMps = 2.5;
        double areaSqm = totalFlowLps / 1000.0 / velocityMps;
        double diameterMm = Math.Sqrt(4 * areaSqm / Math.PI) * 1000;
        return SnapToStandardDn(Math.Max(diameterMm, 50));
    }

    private static double SnapToStandardDn(double diameterMm)
    {
        foreach (var dn in StandardDnMm)
        {
            if (dn >= diameterMm) return dn;
        }
        return StandardDnMm[^1];
    }

    private enum PrincipalAxis { X, Y }

    private record HazardCriteria(
        double CoveragePerHeadM2,
        double MaxSpacingM,
        double DesignAreaM2,
        double MinDensityMmPerMin);
}
