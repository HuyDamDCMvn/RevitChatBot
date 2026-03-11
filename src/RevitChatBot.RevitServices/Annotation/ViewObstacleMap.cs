using Autodesk.Revit.DB;

namespace RevitChatBot.RevitServices.Annotation;

/// <summary>
/// Grid-based spatial index of all visible obstacles in a view.
/// Tags, dimensions, text notes, detail items, and model elements are all tracked
/// so annotation skills can find free zones and avoid overlap with everything.
///
/// Grid cell size is adaptive based on average tag size (~0.3 ft default).
/// Coordinates are 2D (X, Y) — Z is ignored for annotation layout.
/// </summary>
public class ViewObstacleMap
{
    private readonly double _cellSize;
    private readonly double _originX;
    private readonly double _originY;
    private readonly int _cols;
    private readonly int _rows;
    private readonly List<ObstacleRect>[,] _grid;
    private readonly List<ObstacleRect> _allObstacles;

    public int ObstacleCount => _allObstacles.Count;
    public double ViewMinX => _originX;
    public double ViewMinY => _originY;
    public double ViewMaxX => _originX + _cols * _cellSize;
    public double ViewMaxY => _originY + _rows * _cellSize;

    private ViewObstacleMap(double originX, double originY, int cols, int rows, double cellSize)
    {
        _originX = originX;
        _originY = originY;
        _cols = cols;
        _rows = rows;
        _cellSize = cellSize;
        _grid = new List<ObstacleRect>[cols, rows];
        _allObstacles = [];

        for (int x = 0; x < cols; x++)
            for (int y = 0; y < rows; y++)
                _grid[x, y] = [];
    }

    /// <summary>
    /// Build an obstacle map from all visible elements in the given view.
    /// Excludes elements in <paramref name="excludeIds"/> (typically the tags being arranged).
    /// </summary>
    public static ViewObstacleMap Build(Document doc, View view, ISet<long>? excludeIds = null, double cellSize = 0.3)
    {
        excludeIds ??= new HashSet<long>();

        var viewBB = view.get_BoundingBox(view) ?? view.get_BoundingBox(null);
        double minX = viewBB?.Min.X ?? -500;
        double minY = viewBB?.Min.Y ?? -500;
        double maxX = viewBB?.Max.X ?? 500;
        double maxY = viewBB?.Max.Y ?? 500;

        var cropShape = GetCropRegionBounds(view);
        if (cropShape is not null)
        {
            minX = cropShape.Value.minX;
            minY = cropShape.Value.minY;
            maxX = cropShape.Value.maxX;
            maxY = cropShape.Value.maxY;
        }

        double pad = cellSize * 20;
        minX -= pad; minY -= pad;
        maxX += pad; maxY += pad;

        int cols = Math.Max(1, (int)Math.Ceiling((maxX - minX) / cellSize));
        int rows = Math.Max(1, (int)Math.Ceiling((maxY - minY) / cellSize));
        cols = Math.Min(cols, 4000);
        rows = Math.Min(rows, 4000);

        var map = new ViewObstacleMap(minX, minY, cols, rows, cellSize);

        var allElements = new FilteredElementCollector(doc, view.Id).ToElements();
        foreach (var elem in allElements)
        {
            if (excludeIds.Contains(elem.Id.Value)) continue;
            if (elem is ElementType) continue;

            var bb = elem.get_BoundingBox(view);
            if (bb is null) continue;

            var category = ClassifyElement(elem);
            if (category == ObstacleCategory.Ignored) continue;

            var rect = new ObstacleRect
            {
                ElementId = elem.Id.Value,
                MinX = bb.Min.X,
                MinY = bb.Min.Y,
                MaxX = bb.Max.X,
                MaxY = bb.Max.Y,
                Category = category
            };

            map.InsertObstacle(rect);
        }

        return map;
    }

    /// <summary>
    /// Check if an axis-aligned rectangle is completely free of obstacles.
    /// </summary>
    public bool IsAreaFree(double minX, double minY, double maxX, double maxY)
    {
        foreach (var obs in GetObstaclesInArea(minX, minY, maxX, maxY))
        {
            if (Overlaps(minX, minY, maxX, maxY, obs))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Returns all obstacles whose bounding box overlaps the query rectangle.
    /// </summary>
    public List<ObstacleRect> GetObstaclesInArea(double minX, double minY, double maxX, double maxY)
    {
        var result = new List<ObstacleRect>();
        var seen = new HashSet<long>();

        int c0 = CellX(minX), c1 = CellX(maxX);
        int r0 = CellY(minY), r1 = CellY(maxY);

        for (int cx = c0; cx <= c1; cx++)
        {
            for (int ry = r0; ry <= r1; ry++)
            {
                if (cx < 0 || cx >= _cols || ry < 0 || ry >= _rows) continue;
                foreach (var obs in _grid[cx, ry])
                {
                    if (!seen.Add(obs.ElementId)) continue;
                    if (Overlaps(minX, minY, maxX, maxY, obs))
                        result.Add(obs);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Find free rectangular zones near a point, radiating outward in 8 directions.
    /// Returns candidate positions sorted by distance from the center point.
    /// </summary>
    public List<CandidatePosition> FindFreeZones(double centerX, double centerY,
        double tagWidth, double tagHeight, double searchRadius = 3.0, int steps = 12)
    {
        var candidates = new List<CandidatePosition>();
        double halfW = tagWidth / 2;
        double halfH = tagHeight / 2;

        double[] angles = { 90, 0, 45, 135, 180, 270, 315, 225 };

        for (int step = 1; step <= steps; step++)
        {
            double dist = step * searchRadius / steps;
            foreach (var angleDeg in angles)
            {
                double rad = angleDeg * Math.PI / 180.0;
                double cx = centerX + dist * Math.Cos(rad);
                double cy = centerY + dist * Math.Sin(rad);

                double rMinX = cx - halfW, rMinY = cy - halfH;
                double rMaxX = cx + halfW, rMaxY = cy + halfH;

                var obstacles = GetObstaclesInArea(rMinX, rMinY, rMaxX, rMaxY);
                int overlapCount = obstacles.Count(o => Overlaps(rMinX, rMinY, rMaxX, rMaxY, o));

                candidates.Add(new CandidatePosition
                {
                    X = cx,
                    Y = cy,
                    Distance = dist,
                    AngleDegrees = angleDeg,
                    OverlapCount = overlapCount
                });
            }
        }

        return candidates
            .OrderBy(c => c.OverlapCount)
            .ThenBy(c => c.Distance)
            .ToList();
    }

    /// <summary>
    /// Count how many obstacles overlap the given rectangle.
    /// </summary>
    public int CountOverlaps(double minX, double minY, double maxX, double maxY)
    {
        return GetObstaclesInArea(minX, minY, maxX, maxY)
            .Count(o => Overlaps(minX, minY, maxX, maxY, o));
    }

    /// <summary>
    /// Dynamically add an obstacle (e.g., after placing a tag, add it to the map).
    /// </summary>
    public void AddObstacle(long elementId, double minX, double minY, double maxX, double maxY,
        ObstacleCategory category = ObstacleCategory.Tag)
    {
        var rect = new ObstacleRect
        {
            ElementId = elementId,
            MinX = minX, MinY = minY,
            MaxX = maxX, MaxY = maxY,
            Category = category
        };
        InsertObstacle(rect);
    }

    /// <summary>
    /// Remove an obstacle by element ID (e.g., when re-positioning a tag).
    /// </summary>
    public void RemoveObstacle(long elementId)
    {
        _allObstacles.RemoveAll(o => o.ElementId == elementId);
        for (int x = 0; x < _cols; x++)
            for (int y = 0; y < _rows; y++)
                _grid[x, y].RemoveAll(o => o.ElementId == elementId);
    }

    private void InsertObstacle(ObstacleRect rect)
    {
        _allObstacles.Add(rect);

        int c0 = CellX(rect.MinX), c1 = CellX(rect.MaxX);
        int r0 = CellY(rect.MinY), r1 = CellY(rect.MaxY);

        for (int cx = c0; cx <= c1; cx++)
        {
            for (int ry = r0; ry <= r1; ry++)
            {
                if (cx < 0 || cx >= _cols || ry < 0 || ry >= _rows) continue;
                _grid[cx, ry].Add(rect);
            }
        }
    }

    private int CellX(double x) => Math.Clamp((int)((x - _originX) / _cellSize), 0, _cols - 1);
    private int CellY(double y) => Math.Clamp((int)((y - _originY) / _cellSize), 0, _rows - 1);

    private static bool Overlaps(double minX, double minY, double maxX, double maxY, ObstacleRect obs)
    {
        return minX < obs.MaxX && maxX > obs.MinX && minY < obs.MaxY && maxY > obs.MinY;
    }

    private static ObstacleCategory ClassifyElement(Element elem)
    {
        if (elem is IndependentTag) return ObstacleCategory.Tag;
        if (elem is TextNote) return ObstacleCategory.TextNote;
        if (elem is Dimension) return ObstacleCategory.Dimension;
        if (elem is DetailCurve or DetailArc) return ObstacleCategory.DetailLine;

        var cat = elem.Category;
        if (cat is null) return ObstacleCategory.Ignored;

        var bic = cat.BuiltInCategory;
        return bic switch
        {
            BuiltInCategory.OST_DuctCurves or BuiltInCategory.OST_PipeCurves
                or BuiltInCategory.OST_FlexDuctCurves or BuiltInCategory.OST_FlexPipeCurves
                or BuiltInCategory.OST_Conduit or BuiltInCategory.OST_CableTray
                => ObstacleCategory.LinearMep,

            BuiltInCategory.OST_DuctFitting or BuiltInCategory.OST_PipeFitting
                or BuiltInCategory.OST_DuctAccessory or BuiltInCategory.OST_PipeAccessory
                or BuiltInCategory.OST_ConduitFitting or BuiltInCategory.OST_CableTrayFitting
                => ObstacleCategory.Fitting,

            BuiltInCategory.OST_MechanicalEquipment or BuiltInCategory.OST_ElectricalEquipment
                or BuiltInCategory.OST_PlumbingFixtures or BuiltInCategory.OST_Sprinklers
                => ObstacleCategory.Equipment,

            BuiltInCategory.OST_Walls or BuiltInCategory.OST_Floors
                or BuiltInCategory.OST_Ceilings or BuiltInCategory.OST_Columns
                or BuiltInCategory.OST_StructuralColumns or BuiltInCategory.OST_StructuralFraming
                => ObstacleCategory.Architectural,

            BuiltInCategory.OST_Grids => ObstacleCategory.Grid,

            _ => ObstacleCategory.Other
        };
    }

    private static (double minX, double minY, double maxX, double maxY)? GetCropRegionBounds(View view)
    {
        try
        {
            if (!view.CropBoxActive) return null;
            var cropBox = view.CropBox;
            if (cropBox is null) return null;
            return (cropBox.Min.X, cropBox.Min.Y, cropBox.Max.X, cropBox.Max.Y);
        }
        catch
        {
            return null;
        }
    }
}

public class ObstacleRect
{
    public long ElementId { get; set; }
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public ObstacleCategory Category { get; set; }

    public double CenterX => (MinX + MaxX) / 2;
    public double CenterY => (MinY + MaxY) / 2;
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
}

public class CandidatePosition
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Distance { get; set; }
    public double AngleDegrees { get; set; }
    public int OverlapCount { get; set; }
}

public enum ObstacleCategory
{
    Tag,
    TextNote,
    Dimension,
    DetailLine,
    LinearMep,
    Fitting,
    Equipment,
    Architectural,
    Grid,
    Other,
    Ignored
}
