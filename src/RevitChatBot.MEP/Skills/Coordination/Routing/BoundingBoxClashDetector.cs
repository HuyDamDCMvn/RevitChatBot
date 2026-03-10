using Autodesk.Revit.DB;

namespace RevitChatBot.MEP.Skills.Coordination.Routing;

/// <summary>
/// Bounding-box-based clash detection between two sets of elements with tolerance.
/// Uses an axis-aligned bounding box overlap test with configurable clearance.
/// </summary>
public static class BoundingBoxClashDetector
{
    public static bool ElementsClash(Element a, Element b, double toleranceFeet)
    {
        var bb1 = a.get_BoundingBox(null);
        var bb2 = b.get_BoundingBox(null);
        if (bb1 is null || bb2 is null) return false;

        return bb1.Min.X - toleranceFeet <= bb2.Max.X
            && bb1.Max.X + toleranceFeet >= bb2.Min.X
            && bb1.Min.Y - toleranceFeet <= bb2.Max.Y
            && bb1.Max.Y + toleranceFeet >= bb2.Min.Y
            && bb1.Min.Z - toleranceFeet <= bb2.Max.Z
            && bb1.Max.Z + toleranceFeet >= bb2.Min.Z;
    }

    /// <returns>Pairs of (shiftElement, standElement) that clash</returns>
    public static List<(Element Shift, Element Stand)> FindClashPairs(
        IReadOnlyList<Element> shiftElements,
        IReadOnlyList<Element> standElements,
        double toleranceFeet)
    {
        var pairs = new List<(Element, Element)>();

        foreach (var shift in shiftElements)
        {
            foreach (var stand in standElements)
            {
                if (shift.Id == stand.Id) continue;
                if (ElementsClash(shift, stand, toleranceFeet))
                    pairs.Add((shift, stand));
            }
        }

        return pairs;
    }

    /// <summary>
    /// Compute the overlap volume between two bounding boxes (0 if no overlap).
    /// </summary>
    public static double OverlapVolume(BoundingBoxXYZ a, BoundingBoxXYZ b)
    {
        double dx = Math.Max(0, Math.Min(a.Max.X, b.Max.X) - Math.Max(a.Min.X, b.Min.X));
        double dy = Math.Max(0, Math.Min(a.Max.Y, b.Max.Y) - Math.Max(a.Min.Y, b.Min.Y));
        double dz = Math.Max(0, Math.Min(a.Max.Z, b.Max.Z) - Math.Max(a.Min.Z, b.Min.Z));
        return dx * dy * dz;
    }
}
