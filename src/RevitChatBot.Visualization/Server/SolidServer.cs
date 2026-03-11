using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using RevitChatBot.Visualization.Rendering;

namespace RevitChatBot.Visualization.Server;

/// <summary>
/// Renders solids via face decomposition + triangulation.
/// Primary use: highlight element volumes, show clash overlap regions.
/// </summary>
public class SolidServer : VisualizationServer<Solid>
{
    public SolidServer() : base("ChatBot.Solid") { }

    protected override void RenderItem(
        VisualizationItem<Solid> item, View view, DisplayStyle displayStyle)
    {
        var solid = item.Geometry;
        if (solid.Volume < 1e-9) return;

        var allVertices = new List<XYZ>();
        var allNormals = new List<XYZ>();

        foreach (Face face in solid.Faces)
        {
            var (vertices, normals) = RenderHelper.TessellateFace(face);
            allVertices.AddRange(vertices);
            allNormals.AddRange(normals);
        }

        if (allVertices.Count < 3) return;

        var color = item.Style.ToColorWithTransparency();
        int vertexCount = allVertices.Count;
        int triangleCount = vertexCount / 3;

        var formatBits = VertexFormatBits.PositionNormal;
        var vertexBuffer = new VertexBuffer(vertexCount * VertexPositionNormal.GetSizeInFloats());
        vertexBuffer.Map(vertexCount * VertexPositionNormal.GetSizeInFloats());

        var vStream = vertexBuffer.GetVertexStreamPositionNormal();
        for (int i = 0; i < vertexCount; i++)
            vStream.AddVertex(new VertexPositionNormal(allVertices[i], allNormals[i]));
        vertexBuffer.Unmap();

        var indexBuffer = new IndexBuffer(vertexCount);
        indexBuffer.Map(vertexCount);
        var iStream = indexBuffer.GetIndexStreamTriangle();
        for (int i = 0; i < triangleCount; i++)
            iStream.AddTriangle(new IndexTriangle(i * 3, i * 3 + 1, i * 3 + 2));
        indexBuffer.Unmap();

        var effect = new EffectInstance(formatBits);
        effect.SetColor(color.GetColor());
        effect.SetTransparency(color.GetTransparency() / 255.0);

        DrawContext.FlushBuffer(
            vertexBuffer, vertexCount,
            indexBuffer, vertexCount,
            new VertexFormat(formatBits), effect,
            PrimitiveType.TriangleList, 0, triangleCount);
    }

    protected override (XYZ Min, XYZ Max) GetItemBounds(Solid geometry)
    {
        var bb = geometry.GetBoundingBox();
        return (bb.Min, bb.Max);
    }
}
