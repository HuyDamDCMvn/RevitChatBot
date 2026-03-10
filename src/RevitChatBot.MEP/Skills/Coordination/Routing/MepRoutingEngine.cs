using Autodesk.Revit.DB;

namespace RevitChatBot.MEP.Skills.Coordination.Routing;

public sealed class RerouteResult
{
    public int TotalClashPairs { get; set; }
    public int ClashGroups { get; set; }
    public int SuccessfulReroutes { get; set; }
    public int FailedReroutes { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<ReroutedElementInfo> ReroutedElements { get; set; } = [];
}

public sealed class ReroutedElementInfo
{
    public long OriginalElementId { get; set; }
    public string Category { get; set; } = "";
    public string Direction { get; set; } = "";
    public int NewSegments { get; set; }
    public int Fittings { get; set; }
}

/// <summary>
/// Orchestrates the full MEP rerouting pipeline:
///   1. Detect clashes (BoundingBox + tolerance)
///   2. Group into connected components (BFS)
///   3. Classify direction per group
///   4. Compute dogleg geometry
///   5. Create new segments + fittings
///   6. Delete originals on success
/// </summary>
public sealed class MepRoutingEngine
{
    private readonly Document _doc;
    private readonly double _toleranceFeet;
    private readonly double _offsetFeet;
    private readonly RouteDirection _parallelDefault;
    private readonly RouteDirection _perpendicularDefault;

    public MepRoutingEngine(
        Document doc,
        double toleranceMm = 50,
        double offsetMm = 150,
        RouteDirection parallelDefault = RouteDirection.Down,
        RouteDirection perpendicularDefault = RouteDirection.Right)
    {
        _doc = doc;
        _toleranceFeet = toleranceMm / 304.8;
        _offsetFeet = offsetMm / 304.8;
        _parallelDefault = parallelDefault;
        _perpendicularDefault = perpendicularDefault;
    }

    public RerouteResult Execute(
        IReadOnlyList<Element> shiftElements,
        IReadOnlyList<Element> standElements)
    {
        var result = new RerouteResult();

        var clashPairs = BoundingBoxClashDetector.FindClashPairs(
            shiftElements, standElements, _toleranceFeet);
        result.TotalClashPairs = clashPairs.Count;

        if (clashPairs.Count == 0)
            return result;

        var groups = ConnectedComponentAnalyzer.BuildClashGroups(clashPairs);
        result.ClashGroups = groups.Count;

        foreach (var group in groups)
        {
            foreach (var shiftElem in group.ShiftElements)
            {
                try
                {
                    bool success = RerouteElement(shiftElem, group, result);
                    if (success)
                        result.SuccessfulReroutes++;
                    else
                        result.FailedReroutes++;
                }
                catch (Exception ex)
                {
                    result.FailedReroutes++;
                    result.Errors.Add($"Element {shiftElem.Id.Value}: {ex.Message}");
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Analyze clashes without modifying the model (dry run).
    /// </summary>
    public RerouteResult Analyze(
        IReadOnlyList<Element> shiftElements,
        IReadOnlyList<Element> standElements)
    {
        var result = new RerouteResult();

        var clashPairs = BoundingBoxClashDetector.FindClashPairs(
            shiftElements, standElements, _toleranceFeet);
        result.TotalClashPairs = clashPairs.Count;

        var groups = ConnectedComponentAnalyzer.BuildClashGroups(clashPairs);
        result.ClashGroups = groups.Count;

        foreach (var group in groups)
        {
            foreach (var shiftElem in group.ShiftElements)
            {
                var relation = DirectionClassifier.ClassifyRelation(
                    shiftElem, group.StandElements[0]);
                var direction = DirectionClassifier.ChooseDirection(
                    relation, _parallelDefault, _perpendicularDefault);

                result.ReroutedElements.Add(new ReroutedElementInfo
                {
                    OriginalElementId = shiftElem.Id.Value,
                    Category = shiftElem.Category?.Name ?? "Unknown",
                    Direction = direction.ToString(),
                    NewSegments = 5,
                    Fittings = 4
                });
            }
        }

        return result;
    }

    private bool RerouteElement(Element shiftElem, ClashGroup group, RerouteResult result)
    {
        if (shiftElem.Location is not LocationCurve locCurve)
        {
            result.Errors.Add($"Element {shiftElem.Id.Value}: Not a curve-based element, cannot reroute.");
            return false;
        }

        var curve = locCurve.Curve;
        var startPt = curve.GetEndPoint(0);
        var endPt = curve.GetEndPoint(1);

        var relation = DirectionClassifier.ClassifyRelation(
            shiftElem, group.StandElements[0]);
        var direction = DirectionClassifier.ChooseDirection(
            relation, _parallelDefault, _perpendicularDefault);

        XYZ[]? points;
        if (group.UnionStandBounds is not null)
        {
            points = DoglegGeometry.ComputeDoglegPoints(
                startPt, endPt, group.UnionStandBounds, _offsetFeet, direction);
        }
        else
        {
            points = DoglegGeometry.ComputeSimpleDoglegPoints(
                startPt, endPt, 0.25, 0.75, _offsetFeet, direction);
        }

        if (points is null)
        {
            result.Errors.Add($"Element {shiftElem.Id.Value}: Could not compute dogleg geometry.");
            return false;
        }

        return CreateDoglegRoute(shiftElem, points, direction, result);
    }

    private bool CreateDoglegRoute(
        Element original, XYZ[] points, RouteDirection direction, RerouteResult result)
    {
        var segments = new List<MEPCurve>();
        bool allCreated = true;

        for (int i = 0; i < 5; i++)
        {
            var segment = MepSegmentFactory.CreateSegmentLike(_doc, original, points[i], points[i + 1]);
            if (segment is not null)
            {
                segments.Add(segment);
            }
            else
            {
                allCreated = false;
                break;
            }
        }

        if (!allCreated || segments.Count != 5)
        {
            Rollback(segments);
            result.Errors.Add($"Element {original.Id.Value}: Failed to create all 5 segments.");
            return false;
        }

        _doc.Regenerate();

        var junctions = new[] { points[1], points[2], points[3], points[4] };
        int connected = FittingConnector.ConnectSegmentChain(_doc, segments, junctions);

        try
        {
            _doc.Delete(original.Id);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Element {original.Id.Value}: Rerouted but failed to delete original: {ex.Message}");
        }

        result.ReroutedElements.Add(new ReroutedElementInfo
        {
            OriginalElementId = original.Id.Value,
            Category = original.Category?.Name ?? "Unknown",
            Direction = direction.ToString(),
            NewSegments = segments.Count,
            Fittings = connected
        });

        return true;
    }

    private void Rollback(List<MEPCurve> segments)
    {
        foreach (var seg in segments)
        {
            try { _doc.Delete(seg.Id); }
            catch { /* best effort cleanup */ }
        }
    }
}
