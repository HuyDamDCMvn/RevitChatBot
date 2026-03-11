using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using RevitChatBot.Visualization.Rendering;

namespace RevitChatBot.Visualization.Server;

/// <summary>
/// Renders bounding boxes as wireframe cubes via DirectContext3D.
/// Primary use: highlight clashing elements, clearance zones, spatial analysis.
/// </summary>
public class BoundingBoxServer : VisualizationServer<BoundingBoxXYZ>
{
    public BoundingBoxServer() : base("ChatBot.BoundingBox") { }

    protected override void RenderItem(
        VisualizationItem<BoundingBoxXYZ> item, View view, DisplayStyle displayStyle)
    {
        var edges = RenderHelper.GetBoundingBoxEdges(item.Geometry);
        var color = item.Style.ToColorWithTransparency();

        var allPoints = new List<XYZ>();
        foreach (var (start, end) in edges)
        {
            allPoints.Add(start);
            allPoints.Add(end);
        }

        if (allPoints.Count < 2) return;

        int vertexCount = allPoints.Count;
        int lineCount = edges.Count;
        int indexCount = lineCount * 2;

        var formatBits = VertexFormatBits.Position;
        var vertexBuffer = new VertexBuffer(vertexCount * VertexPosition.GetSizeInFloats());
        vertexBuffer.Map(vertexCount * VertexPosition.GetSizeInFloats());

        var vStream = vertexBuffer.GetVertexStreamPosition();
        foreach (var pt in allPoints)
            vStream.AddVertex(new VertexPosition(pt));
        vertexBuffer.Unmap();

        var indexBuffer = new IndexBuffer(indexCount);
        indexBuffer.Map(indexCount);
        var iStream = indexBuffer.GetIndexStreamLine();
        for (int i = 0; i < lineCount; i++)
            iStream.AddLine(new IndexLine(i * 2, i * 2 + 1));
        indexBuffer.Unmap();

        var effect = new EffectInstance(formatBits);
        effect.SetColor(color.GetColor());

        DrawContext.FlushBuffer(
            vertexBuffer, vertexCount,
            indexBuffer, indexCount,
            new VertexFormat(formatBits), effect,
            PrimitiveType.LineList, 0, lineCount);
    }

    protected override (XYZ Min, XYZ Max) GetItemBounds(BoundingBoxXYZ geometry) =>
        (geometry.Min, geometry.Max);
}
