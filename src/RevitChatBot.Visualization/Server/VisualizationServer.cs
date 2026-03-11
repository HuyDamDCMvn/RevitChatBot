using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;

namespace RevitChatBot.Visualization.Server;

/// <summary>
/// Abstract base for DirectContext3D transient rendering servers.
/// Each geometry type (Curve, Solid, BBox, etc.) gets a dedicated server
/// that registers with Revit and redraws every frame while active.
/// 
/// Adapted from RevitDevTool's VisualizationServer pattern for ChatBot use:
/// - Thread-safe geometry add/remove (agent runs on background thread)
/// - Color/transparency per-item for semantic highlighting (red=clash, green=ok)
/// - Metadata tagging for self-learning context capture
/// </summary>
public abstract class VisualizationServer<T> : IDirectContext3DServer, IDisposable
{
    private readonly Guid _serverId = Guid.NewGuid();
    private readonly string _serverName;
    private readonly object _lock = new();
    private readonly List<VisualizationItem<T>> _items = [];
    private bool _isRegistered;
    private Document? _document;

    protected VisualizationServer(string serverName)
    {
        _serverName = serverName;
    }

    public int GeometryCount
    {
        get { lock (_lock) return _items.Count; }
    }

    public Guid GetServerId() => _serverId;
    public string GetName() => _serverName;
    public string GetDescription() => $"RevitChatBot {_serverName}";
    public string GetVendorId() => "RevitChatBot";
    public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
    public string GetApplicationId() => "RevitChatBot";
    public string GetSourceId() => "";
    public bool UsesHandles() => false;

    public bool CanExecute(View dBView) => _items.Count > 0 && _document is not null;
    public bool UseInTransparentPass(View dBView) => true;

    public Outline? GetBoundingBox(View dBView)
    {
        lock (_lock)
        {
            if (_items.Count == 0) return null;
            try
            {
                XYZ min = new(double.MaxValue, double.MaxValue, double.MaxValue);
                XYZ max = new(double.MinValue, double.MinValue, double.MinValue);

                foreach (var item in _items)
                {
                    var (itemMin, itemMax) = GetItemBounds(item.Geometry);
                    min = new XYZ(
                        Math.Min(min.X, itemMin.X),
                        Math.Min(min.Y, itemMin.Y),
                        Math.Min(min.Z, itemMin.Z));
                    max = new XYZ(
                        Math.Max(max.X, itemMax.X),
                        Math.Max(max.Y, itemMax.Y),
                        Math.Max(max.Z, itemMax.Z));
                }

                return new Outline(min, max);
            }
            catch
            {
                return null;
            }
        }
    }

    public void RenderScene(View dBView, DisplayStyle displayStyle)
    {
        lock (_lock)
        {
            foreach (var item in _items)
            {
                try
                {
                    RenderItem(item, dBView, displayStyle);
                }
                catch
                {
                    // skip items that fail to render
                }
            }
        }
    }

    public void AddGeometry(T geometry, VisualizationStyle? style = null, string? tag = null)
    {
        var item = new VisualizationItem<T>
        {
            Geometry = geometry,
            Style = style ?? VisualizationStyle.Default,
            Tag = tag,
            AddedAt = DateTime.UtcNow
        };

        lock (_lock)
        {
            _items.Add(item);
        }
    }

    public void AddGeometries(IEnumerable<T> geometries, VisualizationStyle? style = null, string? tag = null)
    {
        var s = style ?? VisualizationStyle.Default;
        lock (_lock)
        {
            foreach (var g in geometries)
            {
                _items.Add(new VisualizationItem<T>
                {
                    Geometry = g,
                    Style = s,
                    Tag = tag,
                    AddedAt = DateTime.UtcNow
                });
            }
        }
    }

    public void ClearGeometry()
    {
        lock (_lock) _items.Clear();
    }

    public void ClearByTag(string tag)
    {
        lock (_lock) _items.RemoveAll(i => i.Tag == tag);
    }

    public List<VisualizationItem<T>> GetItems()
    {
        lock (_lock) return [.. _items];
    }

    public void Register(Document document)
    {
        if (_isRegistered) return;
        _document = document;

        var directContext3DService = ExternalServiceRegistry
            .GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService)
            as MultiServerService;
        directContext3DService?.AddServer(this);
        _isRegistered = true;
    }

    public void Unregister()
    {
        if (!_isRegistered) return;

        var directContext3DService = ExternalServiceRegistry
            .GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService)
            as MultiServerService;
        directContext3DService?.RemoveServer(_serverId);

        ClearGeometry();
        _isRegistered = false;
        _document = null;
    }

    protected abstract void RenderItem(
        VisualizationItem<T> item, View view, DisplayStyle displayStyle);

    protected abstract (XYZ Min, XYZ Max) GetItemBounds(T geometry);

    public void Dispose()
    {
        Unregister();
    }
}

public class VisualizationItem<T>
{
    public required T Geometry { get; init; }
    public VisualizationStyle Style { get; init; } = VisualizationStyle.Default;
    public string? Tag { get; init; }
    public DateTime AddedAt { get; init; }
}
