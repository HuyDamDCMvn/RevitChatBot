namespace RevitChatBot.Core.Skills;

/// <summary>
/// General-purpose route data for auto-connect skills (HVAC, Plumbing, etc.).
/// Reuses <see cref="PointData"/> and <see cref="SegmentData"/> from SprinklerRouteData.
/// Core stays free of Revit API dependencies.
/// </summary>
public class MepAutoRouteData
{
    public string Domain { get; set; } = "";
    public string ElementCategory { get; set; } = "";
    public List<PointData> EndpointPositions { get; set; } = [];
    public List<SegmentData> BranchSegments { get; set; } = [];
    public List<SegmentData> MainSegments { get; set; } = [];
    public List<PointData> FittingPositions { get; set; } = [];

    public int TotalEndpoints { get; set; }
    public int TotalSegments { get; set; }
    public double TotalLengthFeet { get; set; }
    public int TotalFittings { get; set; }
    public string? LevelName { get; set; }

    /// <summary>Equivalent diameter for round sizing (mm).</summary>
    public double MainSizeMm { get; set; }
    public double BranchSizeMm { get; set; }

    /// <summary>For rectangular ducts — nullable; null means round.</summary>
    public double? MainWidthMm { get; set; }
    public double? MainHeightMm { get; set; }
    public double? BranchWidthMm { get; set; }
    public double? BranchHeightMm { get; set; }

    /// <summary>For drainage pipes — slope as ratio (0.01 = 1%).</summary>
    public double SlopeRatio { get; set; }

    /// <summary>Total airflow or flow rate for the routed network.</summary>
    public double TotalFlowLps { get; set; }

    public bool IsRectangular => MainWidthMm.HasValue && MainHeightMm.HasValue;
}
