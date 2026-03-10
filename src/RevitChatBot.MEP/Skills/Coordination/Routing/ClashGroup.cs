using Autodesk.Revit.DB;

namespace RevitChatBot.MEP.Skills.Coordination.Routing;

/// <summary>
/// A connected component of clashing elements: shift elements that need to move
/// and stand elements they collide with.
/// </summary>
public sealed class ClashGroup
{
    public List<Element> ShiftElements { get; init; } = [];
    public List<Element> StandElements { get; init; } = [];
    public BoundingBoxXYZ? UnionStandBounds { get; set; }
}
