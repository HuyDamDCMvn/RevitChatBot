using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitChatBot.Core.Skills;
using RevitChatBot.Visualization.Rendering;
using RevitChatBot.Visualization.Server;

namespace RevitChatBot.Visualization;

/// <summary>
/// Central manager for all DirectContext3D visualization servers.
/// Provides a unified API for the agent to add/remove/clear geometry with
/// semantic styling. Thread-safe for background agent calls.
///
/// Usage flow:
///   1. Register(doc) once on Revit main thread at startup
///   2. Agent calls AddXxx() methods from any thread
///   3. Call RefreshViews() on main thread to update display
///   4. Agent calls Clear()/ClearByTag() to remove highlights
///
/// Tags enable per-skill cleanup: each skill tags its geometry with its name,
/// so a new skill invocation can clear only its own previous output.
/// </summary>
public class VisualizationManager : IDisposable
{
    private readonly BoundingBoxServer _bboxServer = new();
    private readonly CurveServer _curveServer = new();
    private readonly SolidServer _solidServer = new();

    private Document? _document;
    private UIDocument? _uiDocument;
    private bool _isRegistered;

    private readonly List<VisualizationRecord> _records = [];
    private readonly object _recordLock = new();

    public int TotalGeometryCount =>
        _bboxServer.GeometryCount + _curveServer.GeometryCount + _solidServer.GeometryCount;

    public bool IsRegistered => _isRegistered;

    /// <summary>
    /// Register all servers with Revit. Must be called on the main thread.
    /// </summary>
    public void Register(Document document, UIDocument uiDocument)
    {
        if (_isRegistered) return;
        _document = document;
        _uiDocument = uiDocument;

        _bboxServer.Register(document);
        _curveServer.Register(document);
        _solidServer.Register(document);

        _isRegistered = true;
    }

    /// <summary>
    /// Unregister all servers. Call on shutdown or document close.
    /// </summary>
    public void Unregister()
    {
        _bboxServer.Unregister();
        _curveServer.Unregister();
        _solidServer.Unregister();

        _isRegistered = false;
        _document = null;
        _uiDocument = null;
    }

    // --- Core API: Add geometry with semantic styling ---

    public void AddBoundingBox(BoundingBoxXYZ bbox, VisualizationStyle? style = null, string? tag = null)
    {
        _bboxServer.AddGeometry(bbox, style, tag);
        RecordAction("bbox", tag, style);
    }

    public void AddBoundingBoxes(IEnumerable<BoundingBoxXYZ> boxes, VisualizationStyle? style = null, string? tag = null)
    {
        _bboxServer.AddGeometries(boxes, style, tag);
        RecordAction("bbox_batch", tag, style);
    }

    public void AddCurve(Curve curve, VisualizationStyle? style = null, string? tag = null)
    {
        _curveServer.AddGeometry(curve, style, tag);
        RecordAction("curve", tag, style);
    }

    public void AddCurves(IEnumerable<Curve> curves, VisualizationStyle? style = null, string? tag = null)
    {
        _curveServer.AddGeometries(curves, style, tag);
        RecordAction("curve_batch", tag, style);
    }

    public void AddSolid(Solid solid, VisualizationStyle? style = null, string? tag = null)
    {
        _solidServer.AddGeometry(solid, style, tag);
        RecordAction("solid", tag, style);
    }

    // --- High-level helpers for MEP agent use cases ---

    /// <summary>
    /// Highlight an element's bounding box with a severity-based color.
    /// </summary>
    public void HighlightElement(Element element, string severity, string? tag = null, View? view = null)
    {
        var bbox = element.get_BoundingBox(view);
        if (bbox is null) return;

        var style = VisualizationStyle.FromSeverity(severity);
        _bboxServer.AddGeometry(bbox, style, tag);
        RecordAction("highlight_element", tag, style, element.Id.Value.ToString());
    }

    /// <summary>
    /// Highlight multiple elements, all with the same severity.
    /// </summary>
    public void HighlightElements(
        IEnumerable<Element> elements, string severity, string? tag = null, View? view = null)
    {
        var style = VisualizationStyle.FromSeverity(severity);
        var boxes = new List<BoundingBoxXYZ>();

        foreach (var elem in elements)
        {
            var bbox = elem.get_BoundingBox(view);
            if (bbox is not null) boxes.Add(bbox);
        }

        if (boxes.Count > 0)
            _bboxServer.AddGeometries(boxes, style, tag);

        RecordAction("highlight_elements", tag, style, $"count={boxes.Count}");
    }

    /// <summary>
    /// Visualize a clash pair: both elements highlighted in red,
    /// and the overlap zone emphasized.
    /// </summary>
    public void VisualizeClash(Element elementA, Element elementB, string? tag = null)
    {
        var bboxA = elementA.get_BoundingBox(null);
        var bboxB = elementB.get_BoundingBox(null);
        if (bboxA is null || bboxB is null) return;

        _bboxServer.AddGeometry(bboxA, VisualizationStyle.Clash, tag);
        _bboxServer.AddGeometry(bboxB, VisualizationStyle.Clash, tag);

        var overlapMin = new XYZ(
            Math.Max(bboxA.Min.X, bboxB.Min.X),
            Math.Max(bboxA.Min.Y, bboxB.Min.Y),
            Math.Max(bboxA.Min.Z, bboxB.Min.Z));
        var overlapMax = new XYZ(
            Math.Min(bboxA.Max.X, bboxB.Max.X),
            Math.Min(bboxA.Max.Y, bboxB.Max.Y),
            Math.Min(bboxA.Max.Z, bboxB.Max.Z));

        if (overlapMin.X < overlapMax.X && overlapMin.Y < overlapMax.Y && overlapMin.Z < overlapMax.Z)
        {
            var overlapBox = new BoundingBoxXYZ { Min = overlapMin, Max = overlapMax };
            _bboxServer.AddGeometry(overlapBox, VisualizationStyle.Critical, tag);
        }

        RecordAction("clash_pair", tag, VisualizationStyle.Clash,
            $"{elementA.Id.Value} vs {elementB.Id.Value}");
    }

    /// <summary>
    /// Visualize a routing path as a series of connected curves.
    /// </summary>
    public void VisualizeRoute(IEnumerable<Curve> routeSegments, string? tag = null)
    {
        _curveServer.AddGeometries(routeSegments, VisualizationStyle.RoutingPath, tag);
        RecordAction("route_path", tag, VisualizationStyle.RoutingPath);
    }

    /// <summary>
    /// Render a full sprinkler routing preview from computed route data.
    /// Draws head markers, coverage circles, branch/main/riser pipes,
    /// fitting markers, and blind-spot warnings — all as transient geometry.
    /// </summary>
    public int PreviewSprinklerRoute(SprinklerRouteData routeData, string tag = "sprinkler_preview")
    {
        ClearByTag(tag);
        ClearByTag(tag + "_branch");
        ClearByTag(tag + "_main");
        ClearByTag(tag + "_fittings");
        ClearByTag(tag + "_coverage");

        int rendered = 0;

        foreach (var head in routeData.HeadPositions)
        {
            var pt = new XYZ(head.X, head.Y, head.Z);
            var sphere = RenderHelper.CreateSmallSphere(pt, 0.15);
            if (sphere is not null)
            {
                _solidServer.AddGeometry(sphere, VisualizationStyle.SprinklerHead, tag);
                rendered++;
            }

            if (routeData.CoverageRadiusFeet > 0)
            {
                var circle = RenderHelper.CreateCircle(pt, routeData.CoverageRadiusFeet);
                if (circle is not null)
                    _curveServer.AddGeometry(circle, VisualizationStyle.CoverageArea, tag + "_coverage");
            }
        }

        foreach (var seg in routeData.BranchSegments)
        {
            var line = TryCreateLine(seg);
            if (line is not null)
            {
                _curveServer.AddGeometry(line, VisualizationStyle.RoutingPath, tag + "_branch");
                rendered++;
            }
        }

        foreach (var seg in routeData.MainSegments)
        {
            var line = TryCreateLine(seg);
            if (line is not null)
            {
                _curveServer.AddGeometry(line, VisualizationStyle.CrossMain, tag + "_main");
                rendered++;
            }
        }

        foreach (var fitting in routeData.FittingPositions)
        {
            var pt = new XYZ(fitting.X, fitting.Y, fitting.Z);
            var box = RenderHelper.CreateMarkerBox(pt, 0.12);
            _bboxServer.AddGeometry(box, VisualizationStyle.FittingMarker, tag + "_fittings");
            rendered++;
        }

        RecordAction("sprinkler_preview", tag, VisualizationStyle.RoutingPath,
            $"heads={routeData.TotalHeads}, segments={routeData.TotalSegments}");

        return rendered;
    }

    public void ClearSprinklerPreview(string tag = "sprinkler_preview")
    {
        ClearByTag(tag);
        ClearByTag(tag + "_branch");
        ClearByTag(tag + "_main");
        ClearByTag(tag + "_fittings");
        ClearByTag(tag + "_coverage");
    }

    /// <summary>
    /// Generic MEP route preview for auto-connect skills (HVAC, Plumbing, etc.).
    /// Draws endpoint markers, branch/main segments, and fitting markers.
    /// </summary>
    public int PreviewMepRoute(MepAutoRouteData routeData, string tag = "mep_route_preview")
    {
        ClearByTag(tag);
        ClearByTag(tag + "_branch");
        ClearByTag(tag + "_main");
        ClearByTag(tag + "_fittings");

        int rendered = 0;

        foreach (var ep in routeData.EndpointPositions)
        {
            var pt = new XYZ(ep.X, ep.Y, ep.Z);
            var sphere = RenderHelper.CreateSmallSphere(pt, 0.12);
            if (sphere is not null)
            {
                _solidServer.AddGeometry(sphere, VisualizationStyle.Selection, tag);
                rendered++;
            }
        }

        foreach (var seg in routeData.BranchSegments)
        {
            var line = TryCreateLine(seg);
            if (line is not null)
            {
                _curveServer.AddGeometry(line, VisualizationStyle.RoutingPath, tag + "_branch");
                rendered++;
            }
        }

        foreach (var seg in routeData.MainSegments)
        {
            var line = TryCreateLine(seg);
            if (line is not null)
            {
                _curveServer.AddGeometry(line, VisualizationStyle.CrossMain, tag + "_main");
                rendered++;
            }
        }

        foreach (var fitting in routeData.FittingPositions)
        {
            var pt = new XYZ(fitting.X, fitting.Y, fitting.Z);
            var box = RenderHelper.CreateMarkerBox(pt, 0.10);
            _bboxServer.AddGeometry(box, VisualizationStyle.FittingMarker, tag + "_fittings");
            rendered++;
        }

        RecordAction("mep_route_preview", tag, VisualizationStyle.RoutingPath,
            $"domain={routeData.Domain}, endpoints={routeData.TotalEndpoints}, segments={routeData.TotalSegments}");

        return rendered;
    }

    private static Line? TryCreateLine(SegmentData seg)
    {
        try
        {
            var start = new XYZ(seg.Start.X, seg.Start.Y, seg.Start.Z);
            var end = new XYZ(seg.End.X, seg.End.Y, seg.End.Z);
            if (start.DistanceTo(end) < 0.01) return null;
            return Line.CreateBound(start, end);
        }
        catch
        {
            return null;
        }
    }

    // --- Cleanup ---

    public void Clear()
    {
        _bboxServer.ClearGeometry();
        _curveServer.ClearGeometry();
        _solidServer.ClearGeometry();
    }

    public void ClearByTag(string tag)
    {
        _bboxServer.ClearByTag(tag);
        _curveServer.ClearByTag(tag);
        _solidServer.ClearByTag(tag);
    }

    /// <summary>
    /// Refresh all open views to display updated geometry.
    /// Must be called on the Revit main thread.
    /// </summary>
    public void RefreshViews()
    {
        _uiDocument?.UpdateAllOpenViews();
    }

    // --- Self-learning context: record what was visualized ---

    private void RecordAction(string type, string? tag, VisualizationStyle? style, string? detail = null)
    {
        lock (_recordLock)
        {
            _records.Add(new VisualizationRecord
            {
                GeometryType = type,
                Tag = tag,
                StyleName = GetStyleName(style),
                Detail = detail,
                Timestamp = DateTime.UtcNow
            });

            if (_records.Count > 500)
                _records.RemoveRange(0, _records.Count - 500);
        }
    }

    /// <summary>
    /// Get recent visualization records for self-learning context.
    /// The agent can use this to understand what was previously visualized.
    /// </summary>
    public List<VisualizationRecord> GetRecentRecords(int count = 50)
    {
        lock (_recordLock)
        {
            return _records.TakeLast(count).ToList();
        }
    }

    /// <summary>
    /// Generate a summary of current visualization state for agent context.
    /// </summary>
    public string GetVisualizationSummary()
    {
        var parts = new List<string>();

        if (_bboxServer.GeometryCount > 0)
            parts.Add($"{_bboxServer.GeometryCount} bounding boxes");
        if (_curveServer.GeometryCount > 0)
            parts.Add($"{_curveServer.GeometryCount} curves");
        if (_solidServer.GeometryCount > 0)
            parts.Add($"{_solidServer.GeometryCount} solids");

        if (parts.Count == 0)
            return "[visualization] No geometry currently displayed.";

        var tagSummary = "";
        lock (_recordLock)
        {
            var recentTags = _records
                .Where(r => r.Tag is not null)
                .GroupBy(r => r.Tag!)
                .Select(g => $"{g.Key}: {g.Count()} items")
                .Take(10);
            if (recentTags.Any())
                tagSummary = $"\nActive tags: {string.Join(", ", recentTags)}";
        }

        return $"[visualization] Displaying: {string.Join(", ", parts)}.{tagSummary}";
    }

    private static string GetStyleName(VisualizationStyle? style)
    {
        if (style is null) return "default";
        if (ReferenceEquals(style, VisualizationStyle.Critical)) return "critical";
        if (ReferenceEquals(style, VisualizationStyle.Warning)) return "warning";
        if (ReferenceEquals(style, VisualizationStyle.Ok)) return "ok";
        if (ReferenceEquals(style, VisualizationStyle.Clash)) return "clash";
        if (ReferenceEquals(style, VisualizationStyle.RoutingPath)) return "routing";
        if (ReferenceEquals(style, VisualizationStyle.Selection)) return "selection";
        return "custom";
    }

    public void Dispose()
    {
        Unregister();
    }
}

/// <summary>
/// Records a visualization action for self-learning context.
/// Captures what was visualized, when, and by which skill.
/// </summary>
public class VisualizationRecord
{
    public string GeometryType { get; init; } = "";
    public string? Tag { get; init; }
    public string StyleName { get; init; } = "default";
    public string? Detail { get; init; }
    public DateTime Timestamp { get; init; }
}
