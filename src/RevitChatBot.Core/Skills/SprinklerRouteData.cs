namespace RevitChatBot.Core.Skills;

/// <summary>
/// Serializable route data exchanged between the MEP routing algorithm
/// and the Visualization preview renderer. Uses primitive arrays so that
/// Core stays free of Revit API dependencies.
/// </summary>
public class SprinklerRouteData
{
    public List<PointData> HeadPositions { get; set; } = [];
    public List<SegmentData> BranchSegments { get; set; } = [];
    public List<SegmentData> MainSegments { get; set; } = [];
    public List<PointData> FittingPositions { get; set; } = [];
    public double CoverageRadiusFeet { get; set; }
    public string HazardClass { get; set; } = "light";
    public int TotalHeads { get; set; }
    public int TotalSegments { get; set; }
    public double TotalLengthFeet { get; set; }
    public int TotalFittings { get; set; }
    public string? LevelName { get; set; }
    public double MainDiameterMm { get; set; } = 65;
    public double BranchDiameterMm { get; set; } = 25;
}

public class PointData
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    public PointData() { }

    public PointData(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

public class SegmentData
{
    public PointData Start { get; set; } = new();
    public PointData End { get; set; } = new();
    public double DiameterMm { get; set; }

    public double LengthFeet
    {
        get
        {
            var dx = End.X - Start.X;
            var dy = End.Y - Start.Y;
            var dz = End.Z - Start.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
