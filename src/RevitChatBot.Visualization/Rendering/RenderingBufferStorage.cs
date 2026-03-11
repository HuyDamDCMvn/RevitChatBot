using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;

namespace RevitChatBot.Visualization.Rendering;

/// <summary>
/// Caches tessellated vertex/index buffers for DirectContext3D rendering.
/// Used for complex geometry that is expensive to re-tessellate every frame.
/// The buffer persists across multiple RenderScene callbacks until invalidated.
/// </summary>
public class RenderingBufferStorage : IDisposable
{
    private VertexBuffer? _vertexBuffer;
    private IndexBuffer? _indexBuffer;
    private int _vertexCount;
    private int _indexCount;
    private VertexFormatBits _formatBits;
    private EffectInstance? _effect;
    private bool _dirty = true;

    public bool IsValid => _vertexBuffer is not null && _indexBuffer is not null && _vertexCount > 0;
    public bool IsDirty => _dirty;

    public void Invalidate() => _dirty = true;

    public void SetData(
        VertexBuffer vertexBuffer, int vertexCount,
        IndexBuffer indexBuffer, int indexCount,
        VertexFormatBits formatBits, EffectInstance effect)
    {
        Dispose();
        _vertexBuffer = vertexBuffer;
        _vertexCount = vertexCount;
        _indexBuffer = indexBuffer;
        _indexCount = indexCount;
        _formatBits = formatBits;
        _effect = effect;
        _dirty = false;
    }

    public void FlushTriangles()
    {
        if (!IsValid) return;
        DrawContext.FlushBuffer(
            _vertexBuffer!, _vertexCount,
            _indexBuffer!, _indexCount,
            new VertexFormat(_formatBits), _effect!,
            PrimitiveType.TriangleList, 0, _indexCount / 3);
    }

    public void FlushLines()
    {
        if (!IsValid) return;
        DrawContext.FlushBuffer(
            _vertexBuffer!, _vertexCount,
            _indexBuffer!, _indexCount,
            new VertexFormat(_formatBits), _effect!,
            PrimitiveType.LineList, 0, _indexCount / 2);
    }

    public void Dispose()
    {
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _vertexBuffer = null;
        _indexBuffer = null;
        _vertexCount = 0;
        _indexCount = 0;
        _dirty = true;
    }
}
