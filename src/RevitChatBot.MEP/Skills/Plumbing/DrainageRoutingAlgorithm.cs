using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Plumbing;

/// <summary>
/// Computes a tree-topology drainage pipe layout for plumbing fixtures.
///
/// Strategy:
///   1. Collect fixture positions and DFU (Drainage Fixture Units) per fixture
///   2. Cluster fixtures into groups by proximity
///   3. Route branch pipes from each group toward a main drain line
///   4. Route main horizontal drain connecting all branches
///   5. Apply slope (default 1% for pipes DN≤75, 0.5% for larger)
///
/// Pipe sizing uses UPC/IPC fixture unit method:
///   - Branch pipes: sized by accumulated DFU per group
///   - Main drain: sized by total DFU
/// </summary>
public static class DrainageRoutingAlgorithm
{
    private static readonly double[] StandardDnMm =
    {
        32, 40, 50, 65, 80, 100, 125, 150, 200, 250, 300
    };

    /// <summary>DFU → minimum pipe DN (mm) per UPC Table 7-5.</summary>
    private static readonly (double MaxDfu, double DnMm)[] DfuToSize =
    {
        (1, 32),
        (3, 40),
        (6, 50),
        (12, 65),
        (20, 80),
        (160, 100),
        (360, 125),
        (620, 150),
        (1400, 200),
        (2500, 250),
        (double.MaxValue, 300)
    };

    public static MepAutoRouteData ComputeRoute(
        IReadOnlyList<(XYZ Position, double Dfu)> fixtures,
        double slopePercent = 1.0,
        string? levelName = null)
    {
        var data = new MepAutoRouteData
        {
            Domain = "plumbing",
            ElementCategory = "plumbing_fixture",
            TotalEndpoints = fixtures.Count,
            LevelName = levelName,
            SlopeRatio = slopePercent / 100.0
        };

        if (fixtures.Count == 0)
            return data;

        double totalDfu = fixtures.Sum(f => f.Dfu);
        foreach (var f in fixtures)
            data.EndpointPositions.Add(new PointData(f.Position.X, f.Position.Y, f.Position.Z));

        if (fixtures.Count == 1)
        {
            data.BranchSizeMm = SizePipeByDfu(totalDfu);
            data.MainSizeMm = data.BranchSizeMm;
            return data;
        }

        var positions = fixtures.Select(f => f.Position).ToList();
        var dfuMap = fixtures.ToDictionary(f => f.Position, f => f.Dfu);

        var (principalAxis, groups) = ClusterIntoGroups(positions);
        double referenceZ = positions.Average(p => p.Z);
        double slopeRatio = slopePercent / 100.0;

        var groupCenters = new List<XYZ>();

        foreach (var group in groups)
        {
            if (group.Count == 0) continue;

            var sorted = principalAxis == PrincipalAxis.X
                ? group.OrderBy(p => p.X).ToList()
                : group.OrderBy(p => p.Y).ToList();

            double groupDfu = sorted.Sum(p => dfuMap.GetValueOrDefault(p, 2));
            double branchDn = SizePipeByDfu(groupDfu);

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var start = sorted[i];
                var end = sorted[i + 1];
                double lengthFeet = start.DistanceTo(end);
                double zDrop = lengthFeet * slopeRatio;

                data.BranchSegments.Add(new SegmentData
                {
                    Start = new PointData(start.X, start.Y, start.Z),
                    End = new PointData(end.X, end.Y, end.Z - zDrop),
                    DiameterMm = branchDn
                });
            }

            var centerX = sorted.Average(p => p.X);
            var centerY = sorted.Average(p => p.Y);
            groupCenters.Add(new XYZ(centerX, centerY, referenceZ));

            data.BranchSizeMm = branchDn;
        }

        if (groupCenters.Count >= 2)
        {
            var sortedCenters = principalAxis == PrincipalAxis.X
                ? groupCenters.OrderBy(p => p.Y).ToList()
                : groupCenters.OrderBy(p => p.X).ToList();

            double mainDn = SizePipeByDfu(totalDfu);
            double mainSlope = mainDn > 75 ? 0.005 : slopeRatio;

            for (int i = 0; i < sortedCenters.Count - 1; i++)
            {
                var start = sortedCenters[i];
                var end = sortedCenters[i + 1];
                double lengthFeet = start.DistanceTo(end);
                double zDrop = lengthFeet * mainSlope;

                data.MainSegments.Add(new SegmentData
                {
                    Start = new PointData(start.X, start.Y, start.Z),
                    End = new PointData(end.X, end.Y, end.Z - zDrop),
                    DiameterMm = mainDn
                });
            }

            data.MainSizeMm = mainDn;

            AddBranchToMainConnections(data, groups, sortedCenters, principalAxis, referenceZ, slopeRatio);
        }
        else if (groupCenters.Count == 1)
        {
            data.MainSizeMm = SizePipeByDfu(totalDfu);
        }

        data.TotalSegments = data.BranchSegments.Count + data.MainSegments.Count;
        data.TotalFittings = data.FittingPositions.Count;
        data.TotalLengthFeet = data.BranchSegments.Sum(s => s.LengthFeet)
                             + data.MainSegments.Sum(s => s.LengthFeet);

        return data;
    }

    private static void AddBranchToMainConnections(
        MepAutoRouteData data, List<List<XYZ>> groups, List<XYZ> sortedCenters,
        PrincipalAxis axis, double referenceZ, double slopeRatio)
    {
        foreach (var group in groups)
        {
            if (group.Count == 0) continue;

            var sorted = axis == PrincipalAxis.X
                ? group.OrderBy(p => p.X).ToList()
                : group.OrderBy(p => p.Y).ToList();

            var midX = sorted.Average(p => p.X);
            var midY = sorted.Average(p => p.Y);
            var closestMain = sortedCenters
                .OrderBy(c => Math.Pow(c.X - midX, 2) + Math.Pow(c.Y - midY, 2))
                .First();

            var branchEnd = axis == PrincipalAxis.X
                ? new XYZ(closestMain.X, sorted[0].Y, referenceZ)
                : new XYZ(sorted[0].X, closestMain.Y, referenceZ);

            if (branchEnd.DistanceTo(closestMain) > 0.05)
            {
                double len = branchEnd.DistanceTo(closestMain);
                double zDrop = len * slopeRatio;

                data.BranchSegments.Add(new SegmentData
                {
                    Start = new PointData(branchEnd.X, branchEnd.Y, branchEnd.Z),
                    End = new PointData(closestMain.X, closestMain.Y, closestMain.Z - zDrop),
                    DiameterMm = data.BranchSizeMm
                });
            }

            data.FittingPositions.Add(new PointData(closestMain.X, closestMain.Y, referenceZ));
        }

        foreach (var c in sortedCenters)
            data.FittingPositions.Add(new PointData(c.X, c.Y, c.Z));
    }

    #region Spatial Clustering

    private static (PrincipalAxis axis, List<List<XYZ>> groups) ClusterIntoGroups(
        IReadOnlyList<XYZ> positions)
    {
        double minX = positions.Min(p => p.X), maxX = positions.Max(p => p.X);
        double minY = positions.Min(p => p.Y), maxY = positions.Max(p => p.Y);
        double spanX = maxX - minX;
        double spanY = maxY - minY;

        PrincipalAxis axis;
        double tolerance;

        if (spanX >= spanY)
        {
            axis = PrincipalAxis.X;
            tolerance = spanY > 0 ? spanY / Math.Max(positions.Count / 4.0, 2) : 1.0;
        }
        else
        {
            axis = PrincipalAxis.Y;
            tolerance = spanX > 0 ? spanX / Math.Max(positions.Count / 4.0, 2) : 1.0;
        }

        tolerance = Math.Max(tolerance, 0.5);

        var sorted = axis == PrincipalAxis.X
            ? positions.OrderBy(p => p.Y).ToList()
            : positions.OrderBy(p => p.X).ToList();

        var groups = new List<List<XYZ>>();
        var current = new List<XYZ> { sorted[0] };

        for (int i = 1; i < sorted.Count; i++)
        {
            double coord = axis == PrincipalAxis.X ? sorted[i].Y : sorted[i].X;
            double prev = axis == PrincipalAxis.X ? sorted[i - 1].Y : sorted[i - 1].X;

            if (Math.Abs(coord - prev) > tolerance)
            {
                groups.Add(current);
                current = [];
            }
            current.Add(sorted[i]);
        }
        if (current.Count > 0)
            groups.Add(current);

        return (axis, groups);
    }

    private enum PrincipalAxis { X, Y }

    #endregion

    #region Pipe Sizing by DFU

    private static double SizePipeByDfu(double dfu)
    {
        foreach (var (maxDfu, dn) in DfuToSize)
            if (dfu <= maxDfu) return dn;
        return StandardDnMm[^1];
    }

    #endregion
}
