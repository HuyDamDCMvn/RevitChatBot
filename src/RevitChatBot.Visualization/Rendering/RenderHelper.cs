using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;

namespace RevitChatBot.Visualization.Rendering;

/// <summary>
/// Low-level DirectContext3D drawing helpers that convert Revit geometry
/// into vertex/index buffers for GPU rendering.
/// </summary>
public static class RenderHelper
{
    public static RenderingBufferStorage CreateLineBuffer(
        IList<XYZ> points, ColorWithTransparency color)
    {
        if (points.Count < 2) return new RenderingBufferStorage();

        int vertexCount = points.Count;
        int lineCount = points.Count - 1;
        int indexCount = lineCount * 2;

        var formatBits = VertexFormatBits.Position;
        var vertexBuffer = new VertexBuffer(vertexCount * VertexPosition.GetSizeInFloats());
        vertexBuffer.Map(vertexCount * VertexPosition.GetSizeInFloats());

        var stream = vertexBuffer.GetVertexStreamPosition();
        foreach (var pt in points)
            stream.AddVertex(new VertexPosition(pt));
        vertexBuffer.Unmap();

        var indexBuffer = new IndexBuffer(indexCount);
        indexBuffer.Map(indexCount);
        var indexStream = indexBuffer.GetIndexStreamTriangle();

        for (int i = 0; i < lineCount; i++)
        {
            indexBuffer.GetIndexStreamLine().AddLine(new IndexLine(i, i + 1));
        }
        indexBuffer.Unmap();

        var effect = new EffectInstance(formatBits);
        effect.SetColor(color.GetColor());
        effect.SetTransparency(color.GetTransparency() / 255.0);

        var storage = new RenderingBufferStorage();
        storage.SetData(vertexBuffer, vertexCount, indexBuffer, indexCount, formatBits, effect);
        return storage;
    }

    public static RenderingBufferStorage CreateTriangleBuffer(
        IList<XYZ> vertices, IList<XYZ> normals, ColorWithTransparency color)
    {
        if (vertices.Count < 3) return new RenderingBufferStorage();

        int vertexCount = vertices.Count;
        int triangleCount = vertexCount / 3;
        int indexCount = vertexCount;

        var formatBits = VertexFormatBits.PositionNormal;
        var vertexBuffer = new VertexBuffer(vertexCount * VertexPositionNormal.GetSizeInFloats());
        vertexBuffer.Map(vertexCount * VertexPositionNormal.GetSizeInFloats());

        var stream = vertexBuffer.GetVertexStreamPositionNormal();
        for (int i = 0; i < vertexCount; i++)
        {
            var normal = i < normals.Count ? normals[i] : XYZ.BasisZ;
            stream.AddVertex(new VertexPositionNormal(vertices[i], normal));
        }
        vertexBuffer.Unmap();

        var indexBuffer = new IndexBuffer(indexCount);
        indexBuffer.Map(indexCount);
        var indexStream = indexBuffer.GetIndexStreamTriangle();

        for (int i = 0; i < triangleCount; i++)
            indexStream.AddTriangle(new IndexTriangle(i * 3, i * 3 + 1, i * 3 + 2));
        indexBuffer.Unmap();

        var effect = new EffectInstance(formatBits);
        effect.SetColor(color.GetColor());
        effect.SetTransparency(color.GetTransparency() / 255.0);

        var storage = new RenderingBufferStorage();
        storage.SetData(vertexBuffer, vertexCount, indexBuffer, indexCount, formatBits, effect);
        return storage;
    }

    public static (List<XYZ> Vertices, List<XYZ> Normals) TessellateFace(Face face)
    {
        var mesh = face.Triangulate();
        var vertices = new List<XYZ>();
        var normals = new List<XYZ>();

        for (int i = 0; i < mesh.NumTriangles; i++)
        {
            var triangle = mesh.get_Triangle(i);
            var v0 = triangle.get_Vertex(0);
            var v1 = triangle.get_Vertex(1);
            var v2 = triangle.get_Vertex(2);

            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);

            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var normal = edge1.CrossProduct(edge2).Normalize();
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
        }

        return (vertices, normals);
    }

    public static List<XYZ> TessellateCurve(Curve curve, double tolerance = 0.01)
    {
        var tessellated = curve.Tessellate();
        return tessellated.ToList();
    }

    public static List<(XYZ Start, XYZ End)> GetBoundingBoxEdges(BoundingBoxXYZ bbox)
    {
        var min = bbox.Min;
        var max = bbox.Max;

        var corners = new[]
        {
            new XYZ(min.X, min.Y, min.Z), // 0
            new XYZ(max.X, min.Y, min.Z), // 1
            new XYZ(max.X, max.Y, min.Z), // 2
            new XYZ(min.X, max.Y, min.Z), // 3
            new XYZ(min.X, min.Y, max.Z), // 4
            new XYZ(max.X, min.Y, max.Z), // 5
            new XYZ(max.X, max.Y, max.Z), // 6
            new XYZ(min.X, max.Y, max.Z), // 7
        };

        return
        [
            (corners[0], corners[1]), (corners[1], corners[2]),
            (corners[2], corners[3]), (corners[3], corners[0]),
            (corners[4], corners[5]), (corners[5], corners[6]),
            (corners[6], corners[7]), (corners[7], corners[4]),
            (corners[0], corners[4]), (corners[1], corners[5]),
            (corners[2], corners[6]), (corners[3], corners[7]),
        ];
    }
}
