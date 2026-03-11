using Autodesk.Revit.DB;

namespace RevitChatBot.RevitServices.Annotation;

/// <summary>
/// Scores candidate tag positions based on multiple weighted factors:
///   - Obstacle overlap (heaviest penalty)
///   - Tag-tag collision
///   - Distance from host element
///   - Preferred zone bias (above/right for MEP convention)
///   - Alignment bonus with nearby same-category tags
///   - Leader length penalty
///   - Crop region edge penalty
/// </summary>
public class TagPositionScorer
{
    private readonly ViewObstacleMap _obstacleMap;
    private readonly List<TagPlacement> _placedTags;
    private readonly TagScoringWeights _weights;

    public TagPositionScorer(ViewObstacleMap obstacleMap,
        List<TagPlacement>? existingTags = null,
        TagScoringWeights? weights = null)
    {
        _obstacleMap = obstacleMap;
        _placedTags = existingTags ?? [];
        _weights = weights ?? TagScoringWeights.Default;
    }

    /// <summary>
    /// Score a candidate position for a tag. Higher = better.
    /// </summary>
    public double Score(double candidateX, double candidateY,
        double tagWidth, double tagHeight,
        double elementX, double elementY,
        PreferredZone preferredZone = PreferredZone.Auto,
        BuiltInCategory? taggedCategory = null)
    {
        double score = 100.0;
        double halfW = tagWidth / 2;
        double halfH = tagHeight / 2;
        double minX = candidateX - halfW, maxX = candidateX + halfW;
        double minY = candidateY - halfH, maxY = candidateY + halfH;

        // Factor 1: Obstacle overlap (model elements, text notes, dimensions)
        var obstacles = _obstacleMap.GetObstaclesInArea(
            minX - _weights.ObstaclePadding, minY - _weights.ObstaclePadding,
            maxX + _weights.ObstaclePadding, maxY + _weights.ObstaclePadding);

        foreach (var obs in obstacles)
        {
            double penalty = obs.Category switch
            {
                ObstacleCategory.Equipment => _weights.EquipmentOverlapPenalty,
                ObstacleCategory.LinearMep => _weights.MepOverlapPenalty,
                ObstacleCategory.Fitting => _weights.MepOverlapPenalty * 0.8,
                ObstacleCategory.TextNote => _weights.TextOverlapPenalty,
                ObstacleCategory.Dimension => _weights.DimensionOverlapPenalty,
                ObstacleCategory.Architectural => _weights.ArchOverlapPenalty,
                ObstacleCategory.Grid => _weights.GridOverlapPenalty,
                _ => _weights.OtherOverlapPenalty
            };
            score -= penalty;
        }

        // Factor 2: Tag-tag collision
        foreach (var placed in _placedTags)
        {
            if (OverlapsTag(minX, minY, maxX, maxY, placed))
                score -= _weights.TagCollisionPenalty;
        }

        // Factor 3: Distance from host element
        double dist = Math.Sqrt(
            Math.Pow(candidateX - elementX, 2) + Math.Pow(candidateY - elementY, 2));
        score -= dist * _weights.DistancePenaltyPerFoot;

        // Factor 4: Preferred zone bias
        var effectiveZone = preferredZone == PreferredZone.Auto
            ? ResolveAutoZone(taggedCategory)
            : preferredZone;

        score += ComputeZoneBonus(candidateX, candidateY, elementX, elementY, effectiveZone);

        // Factor 5: Alignment bonus with same-category tags
        if (taggedCategory.HasValue)
        {
            score += ComputeAlignmentBonus(candidateX, candidateY, taggedCategory.Value);
        }

        // Factor 6: Leader length penalty (longer leaders = less clean)
        if (dist > _weights.MaxPreferredLeaderLength)
        {
            double excess = dist - _weights.MaxPreferredLeaderLength;
            score -= excess * _weights.LeaderExcessPenaltyPerFoot;
        }

        // Factor 7: View edge penalty
        double edgeMargin = 0.5;
        if (minX < _obstacleMap.ViewMinX + edgeMargin || maxX > _obstacleMap.ViewMaxX - edgeMargin ||
            minY < _obstacleMap.ViewMinY + edgeMargin || maxY > _obstacleMap.ViewMaxY - edgeMargin)
        {
            score -= _weights.ViewEdgePenalty;
        }

        return score;
    }

    /// <summary>
    /// Find the best position for a tag among pre-generated candidates.
    /// </summary>
    public (double X, double Y, double Score) FindBestPosition(
        double elementX, double elementY,
        double tagWidth, double tagHeight,
        double searchRadius = 3.0,
        PreferredZone preferredZone = PreferredZone.Auto,
        BuiltInCategory? taggedCategory = null)
    {
        var candidates = _obstacleMap.FindFreeZones(
            elementX, elementY, tagWidth, tagHeight, searchRadius);

        double bestScore = double.MinValue;
        double bestX = elementX, bestY = elementY + 0.5;

        foreach (var cand in candidates)
        {
            double s = Score(cand.X, cand.Y, tagWidth, tagHeight,
                elementX, elementY, preferredZone, taggedCategory);
            if (s > bestScore)
            {
                bestScore = s;
                bestX = cand.X;
                bestY = cand.Y;
            }
        }

        return (bestX, bestY, bestScore);
    }

    /// <summary>
    /// Register a placed tag so subsequent scoring accounts for it.
    /// </summary>
    public void RegisterPlacedTag(double minX, double minY, double maxX, double maxY,
        BuiltInCategory? taggedCategory = null)
    {
        _placedTags.Add(new TagPlacement
        {
            MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY,
            TaggedCategory = taggedCategory
        });
    }

    private double ComputeZoneBonus(double cx, double cy, double ex, double ey, PreferredZone zone)
    {
        double bonus = 0;
        switch (zone)
        {
            case PreferredZone.Above:
                if (cy > ey) bonus = _weights.PreferredZoneBonus;
                else if (cy < ey) bonus = -_weights.PreferredZoneBonus * 0.3;
                break;
            case PreferredZone.Below:
                if (cy < ey) bonus = _weights.PreferredZoneBonus;
                else if (cy > ey) bonus = -_weights.PreferredZoneBonus * 0.3;
                break;
            case PreferredZone.Right:
                if (cx > ex) bonus = _weights.PreferredZoneBonus;
                else if (cx < ex) bonus = -_weights.PreferredZoneBonus * 0.3;
                break;
            case PreferredZone.Left:
                if (cx < ex) bonus = _weights.PreferredZoneBonus;
                else if (cx > ex) bonus = -_weights.PreferredZoneBonus * 0.3;
                break;
            case PreferredZone.AboveRight:
                if (cy > ey && cx > ex) bonus = _weights.PreferredZoneBonus;
                else if (cy > ey || cx > ex) bonus = _weights.PreferredZoneBonus * 0.5;
                break;
        }
        return bonus;
    }

    private double ComputeAlignmentBonus(double cx, double cy, BuiltInCategory category)
    {
        double bonus = 0;
        double alignThreshold = 0.15;

        foreach (var placed in _placedTags)
        {
            if (placed.TaggedCategory != category) continue;

            double placedCy = (placed.MinY + placed.MaxY) / 2;
            double placedCx = (placed.MinX + placed.MaxX) / 2;

            if (Math.Abs(cy - placedCy) < alignThreshold)
                bonus += _weights.AlignmentBonus;
            if (Math.Abs(cx - placedCx) < alignThreshold)
                bonus += _weights.AlignmentBonus * 0.7;
        }

        return Math.Min(bonus, _weights.AlignmentBonus * 3);
    }

    private static PreferredZone ResolveAutoZone(BuiltInCategory? category)
    {
        if (category is null) return PreferredZone.Above;
        return category.Value switch
        {
            BuiltInCategory.OST_DuctCurves or BuiltInCategory.OST_FlexDuctCurves
                => PreferredZone.Above,
            BuiltInCategory.OST_PipeCurves or BuiltInCategory.OST_FlexPipeCurves
                => PreferredZone.Above,
            BuiltInCategory.OST_MechanicalEquipment or BuiltInCategory.OST_ElectricalEquipment
                => PreferredZone.AboveRight,
            BuiltInCategory.OST_CableTray or BuiltInCategory.OST_Conduit
                => PreferredZone.Above,
            _ => PreferredZone.Above
        };
    }

    private static bool OverlapsTag(double minX, double minY, double maxX, double maxY, TagPlacement tag)
    {
        return minX < tag.MaxX && maxX > tag.MinX && minY < tag.MaxY && maxY > tag.MinY;
    }
}

public class TagPlacement
{
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public BuiltInCategory? TaggedCategory { get; set; }
}

public class TagScoringWeights
{
    public double EquipmentOverlapPenalty { get; set; } = 35;
    public double MepOverlapPenalty { get; set; } = 30;
    public double TextOverlapPenalty { get; set; } = 25;
    public double DimensionOverlapPenalty { get; set; } = 25;
    public double ArchOverlapPenalty { get; set; } = 15;
    public double GridOverlapPenalty { get; set; } = 10;
    public double OtherOverlapPenalty { get; set; } = 10;
    public double ObstaclePadding { get; set; } = 0.05;

    public double TagCollisionPenalty { get; set; } = 40;
    public double DistancePenaltyPerFoot { get; set; } = 5;
    public double PreferredZoneBonus { get; set; } = 20;
    public double AlignmentBonus { get; set; } = 15;
    public double MaxPreferredLeaderLength { get; set; } = 1.5;
    public double LeaderExcessPenaltyPerFoot { get; set; } = 10;
    public double ViewEdgePenalty { get; set; } = 25;

    public static TagScoringWeights Default => new();
}

public enum PreferredZone
{
    Auto,
    Above,
    Below,
    Left,
    Right,
    AboveRight
}
