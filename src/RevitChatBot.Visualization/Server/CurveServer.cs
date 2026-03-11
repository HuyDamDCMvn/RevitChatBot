using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using RevitChatBot.Visualization.Rendering;

namespace RevitChatBot.Visualization.Server;

/// <summary>
/// Renders curves (lines, arcs, splines) as polylines via DirectContext3D.
/// Primary use: visualize routing paths, system traces, duct/pipe centerlines.
/// </summary>
public class CurveServer : VisualizationServer<Curve>
{
    public CurveServer() : base("ChatBot.Curve") { }

    protected override void RenderItem(
        VisualizationItem<Curve> item, View view, DisplayStyle displayStyle)
    {
        var points = RenderHelper.TessellateCurve(item.Geometry);
        if (points.Count < 2) return;

        var color = item.Style.ToColorWithTransparency();
        int vertexCount = points.Count;
        int lineCount = vertexCount - 1;
        int indexCount = lineCount * 2;

        var formatBits = VertexFormatBits.Position;
        var vertexBuffer = new VertexBuffer(vertexCount * VertexPosition.GetSizeInFloats());
        vertexBuffer.Map(vertexCount * VertexPosition.GetSizeInFloats());

        var vStream = vertexBuffer.GetVertexStreamPosition();
        foreach (var pt in points)
            vStream.AddVertex(new VertexPosition(pt));
        vertexBuffer.Unmap();

        var indexBuffer = new IndexBuffer(indexCount);
        indexBuffer.Map(indexCount);
        var iStream = indexBuffer.GetIndexStreamLine();
        for (int i = 0; i < lineCount; i++)
            iStream.AddLine(new IndexLine(i, i + 1));
        indexBuffer.Unmap();

        var effect = new EffectInstance(formatBits);
        effect.SetColor(color.GetColor());

        DrawContext.FlushBuffer(
            vertexBuffer, vertexCount,
            indexBuffer, indexCount,
            new VertexFormat(formatBits), effect,
            PrimitiveType.LineList, 0, lineCount);
    }

    protected override (XYZ Min, XYZ Max) GetItemBounds(Curve geometry)
    {
        var bb = geometry.GetEndPoint(0);
        var be = geometry.GetEndPoint(1);
        return (
            new XYZ(Math.Min(bb.X, be.X), Math.Min(bb.Y, be.Y), Math.Min(bb.Z, be.Z)),
            new XYZ(Math.Max(bb.X, be.X), Math.Max(bb.Y, be.Y), Math.Max(bb.Z, be.Z)));
    }
}
