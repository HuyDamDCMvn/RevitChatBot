using Autodesk.Revit.DB;

namespace RevitChatBot.RevitServices.Annotation;

/// <summary>
/// Force-directed layout algorithm for arranging tags in a Revit view.
///
/// Three forces act on each tag:
///   1. Attractive force — pulls tag toward its host element (keep leader short)
///   2. Repulsive force  — pushes tag away from obstacles and other tags
///   3. Bias force       — nudges tag toward preferred zone (above/right)
///
/// After force simulation converges, a snap-alignment pass groups tags
/// into horizontal/vertical rows for a clean professional look.
/// </summary>
public class ForceDirectedTagArranger
{
    private const double Epsilon = 1e-9;
    private const double MaxDisplacementPerStep = 0.3;
    private const double ConvergenceThreshold = 0.005;

    private readonly ViewObstacleMap _obstacleMap;
    private readonly ForceLayoutSettings _settings;

    public ForceDirectedTagArranger(ViewObstacleMap obstacleMap, ForceLayoutSettings? settings = null)
    {
        _obstacleMap = obstacleMap;
        _settings = settings ?? ForceLayoutSettings.Default;
    }

    /// <summary>
    /// Run the force-directed layout on the given tag set.
    /// Returns updated positions for each tag (does NOT commit to Revit — caller handles Transaction).
    /// </summary>
    public List<TagLayoutResult> Arrange(List<TagLayoutInput> tags)
    {
        if (tags.Count < 2)
            return tags.Select(t => new TagLayoutResult
            {
                Tag = t.Tag, NewX = t.CurrentX, NewY = t.CurrentY, WasMoved = false
            }).ToList();

        var state = tags.Select(t => new TagState
        {
            Tag = t.Tag,
            X = t.CurrentX,
            Y = t.CurrentY,
            Width = t.Width,
            Height = t.Height,
            HostX = t.HostElementX,
            HostY = t.HostElementY,
            TaggedCategory = t.TaggedCategory,
            OriginalX = t.CurrentX,
            OriginalY = t.CurrentY
        }).ToList();

        int iterationsRun = 0;
        for (int iter = 0; iter < _settings.MaxIterations; iter++)
        {
            iterationsRun++;
            double totalDisplacement = 0;

            for (int i = 0; i < state.Count; i++)
            {
                var tag = state[i];
                double fx = 0, fy = 0;

                // Attractive force toward host element
                var (ax, ay) = ComputeAttractiveForce(tag);
                fx += ax; fy += ay;

                // Repulsive force from other tags
                for (int j = 0; j < state.Count; j++)
                {
                    if (i == j) continue;
                    var (rx, ry) = ComputeTagRepulsion(tag, state[j]);
                    fx += rx; fy += ry;
                }

                // Repulsive force from obstacles
                var (ox, oy) = ComputeObstacleRepulsion(tag);
                fx += ox; fy += oy;

                // Bias force toward preferred zone
                var (bx, by) = ComputeBiasForce(tag);
                fx += bx; fy += by;

                // Apply damping
                double damping = _settings.DampingFactor;
                fx *= damping;
                fy *= damping;

                // Clamp displacement
                double mag = Math.Sqrt(fx * fx + fy * fy);
                if (mag > MaxDisplacementPerStep)
                {
                    fx = fx / mag * MaxDisplacementPerStep;
                    fy = fy / mag * MaxDisplacementPerStep;
                    mag = MaxDisplacementPerStep;
                }

                tag.X += fx;
                tag.Y += fy;
                totalDisplacement += mag;
            }

            if (totalDisplacement / state.Count < ConvergenceThreshold)
                break;
        }

        // Post-process: snap alignment
        if (_settings.EnableSnapAlignment)
            SnapAlign(state);

        return state.Select(s => new TagLayoutResult
        {
            Tag = s.Tag,
            NewX = s.X,
            NewY = s.Y,
            WasMoved = Math.Abs(s.X - s.OriginalX) > Epsilon || Math.Abs(s.Y - s.OriginalY) > Epsilon,
            IterationsUsed = iterationsRun
        }).ToList();
    }

    private (double fx, double fy) ComputeAttractiveForce(TagState tag)
    {
        double dx = tag.HostX - tag.X;
        double dy = tag.HostY - tag.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < _settings.MinAttractiveDistance) return (0, 0);

        double force = _settings.AttractiveStrength * (dist - _settings.MinAttractiveDistance);
        force = Math.Min(force, _settings.MaxAttractiveForce);

        return (force * dx / (dist + Epsilon), force * dy / (dist + Epsilon));
    }

    private (double fx, double fy) ComputeTagRepulsion(TagState a, TagState b)
    {
        double padX = (a.Width + b.Width) / 2 + _settings.TagPadding;
        double padY = (a.Height + b.Height) / 2 + _settings.TagPadding;

        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double absDx = Math.Abs(dx);
        double absDy = Math.Abs(dy);

        if (absDx >= padX || absDy >= padY) return (0, 0);

        double overlapX = padX - absDx;
        double overlapY = padY - absDy;

        double fx = (dx >= 0 ? overlapX : -overlapX) * _settings.RepulsiveStrength;
        double fy = (dy >= 0 ? overlapY : -overlapY) * _settings.RepulsiveStrength;

        return (fx, fy);
    }

    private (double fx, double fy) ComputeObstacleRepulsion(TagState tag)
    {
        double halfW = tag.Width / 2 + _settings.ObstaclePadding;
        double halfH = tag.Height / 2 + _settings.ObstaclePadding;
        double queryMinX = tag.X - halfW, queryMaxX = tag.X + halfW;
        double queryMinY = tag.Y - halfH, queryMaxY = tag.Y + halfH;

        var obstacles = _obstacleMap.GetObstaclesInArea(queryMinX, queryMinY, queryMaxX, queryMaxY);

        double fx = 0, fy = 0;
        foreach (var obs in obstacles)
        {
            double dx = tag.X - obs.CenterX;
            double dy = tag.Y - obs.CenterY;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist < Epsilon) { dx = 0.01; dist = 0.01; }

            double strength = obs.Category switch
            {
                ObstacleCategory.Equipment => _settings.ObstacleRepulsionStrength * 1.5,
                ObstacleCategory.LinearMep => _settings.ObstacleRepulsionStrength,
                ObstacleCategory.TextNote or ObstacleCategory.Dimension
                    => _settings.ObstacleRepulsionStrength * 1.2,
                _ => _settings.ObstacleRepulsionStrength * 0.5
            };

            double force = strength / (dist + 0.1);
            fx += force * dx / (dist + Epsilon);
            fy += force * dy / (dist + Epsilon);
        }

        return (fx, fy);
    }

    private (double fx, double fy) ComputeBiasForce(TagState tag)
    {
        double bx = 0, by = 0;

        var zone = _settings.PreferredZone;
        if (zone == PreferredZone.Auto)
        {
            zone = tag.TaggedCategory switch
            {
                BuiltInCategory.OST_DuctCurves or BuiltInCategory.OST_PipeCurves => PreferredZone.Above,
                BuiltInCategory.OST_MechanicalEquipment => PreferredZone.AboveRight,
                _ => PreferredZone.Above
            };
        }

        double s = _settings.BiasStrength;
        switch (zone)
        {
            case PreferredZone.Above:
                by = tag.Y < tag.HostY + 0.3 ? s : 0;
                break;
            case PreferredZone.Below:
                by = tag.Y > tag.HostY - 0.3 ? -s : 0;
                break;
            case PreferredZone.Right:
                bx = tag.X < tag.HostX + 0.3 ? s : 0;
                break;
            case PreferredZone.Left:
                bx = tag.X > tag.HostX - 0.3 ? -s : 0;
                break;
            case PreferredZone.AboveRight:
                if (tag.Y < tag.HostY + 0.3) by = s * 0.7;
                if (tag.X < tag.HostX + 0.3) bx = s * 0.5;
                break;
        }

        return (bx, by);
    }

    /// <summary>
    /// Post-process: snap tags that are nearly aligned into exact rows.
    /// Groups tags whose Y (or X) differ by less than snapThreshold,
    /// then moves them to the median Y (or X) of the group.
    /// </summary>
    private void SnapAlign(List<TagState> tags)
    {
        double snapThreshold = _settings.SnapAlignmentThreshold;

        // Horizontal alignment (snap Y)
        var sorted = tags.OrderBy(t => t.Y).ToList();
        var groups = new List<List<TagState>>();
        var current = new List<TagState> { sorted[0] };

        for (int i = 1; i < sorted.Count; i++)
        {
            if (Math.Abs(sorted[i].Y - sorted[i - 1].Y) < snapThreshold)
            {
                current.Add(sorted[i]);
            }
            else
            {
                groups.Add(current);
                current = [sorted[i]];
            }
        }
        groups.Add(current);

        foreach (var group in groups.Where(g => g.Count >= 2))
        {
            double medianY = group.OrderBy(t => t.Y).ElementAt(group.Count / 2).Y;
            foreach (var tag in group)
                tag.Y = medianY;
        }
    }

    private class TagState
    {
        public IndependentTag Tag { get; set; } = null!;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double HostX { get; set; }
        public double HostY { get; set; }
        public BuiltInCategory? TaggedCategory { get; set; }
        public double OriginalX { get; set; }
        public double OriginalY { get; set; }
    }
}

public class TagLayoutInput
{
    public IndependentTag Tag { get; set; } = null!;
    public double CurrentX { get; set; }
    public double CurrentY { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double HostElementX { get; set; }
    public double HostElementY { get; set; }
    public BuiltInCategory? TaggedCategory { get; set; }
}

public class TagLayoutResult
{
    public IndependentTag Tag { get; set; } = null!;
    public double NewX { get; set; }
    public double NewY { get; set; }
    public bool WasMoved { get; set; }
    public int IterationsUsed { get; set; }
}

public class ForceLayoutSettings
{
    public int MaxIterations { get; set; } = 80;
    public double DampingFactor { get; set; } = 0.4;
    public double AttractiveStrength { get; set; } = 0.15;
    public double MinAttractiveDistance { get; set; } = 0.3;
    public double MaxAttractiveForce { get; set; } = 0.2;
    public double RepulsiveStrength { get; set; } = 0.5;
    public double TagPadding { get; set; } = 0.12;
    public double ObstacleRepulsionStrength { get; set; } = 0.3;
    public double ObstaclePadding { get; set; } = 0.08;
    public double BiasStrength { get; set; } = 0.05;
    public PreferredZone PreferredZone { get; set; } = PreferredZone.Auto;
    public bool EnableSnapAlignment { get; set; } = true;
    public double SnapAlignmentThreshold { get; set; } = 0.15;

    public static ForceLayoutSettings Default => new();
}
