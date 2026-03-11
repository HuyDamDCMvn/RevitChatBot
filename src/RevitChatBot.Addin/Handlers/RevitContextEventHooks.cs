using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitChatBot.Core.Context;

namespace RevitChatBot.Addin.Handlers;

/// <summary>
/// Subscribes to Revit UI/document events and updates RevitContextCache in real-time.
/// Event handlers ONLY capture state — no document modifications or transactions.
/// </summary>
public class RevitContextEventHooks : IDisposable
{
    private readonly UIApplication _uiApp;
    private readonly RevitContextCache _cache;

    public RevitContextEventHooks(UIApplication uiApp, RevitContextCache cache)
    {
        _uiApp = uiApp;
        _cache = cache;
    }

    public void Subscribe()
    {
        _uiApp.SelectionChanged += OnSelectionChanged;
        _uiApp.ViewActivated += OnViewActivated;
        _uiApp.Application.DocumentChanged += OnDocumentChanged;

        CaptureInitialState();
    }

    public void Unsubscribe()
    {
        _uiApp.SelectionChanged -= OnSelectionChanged;
        _uiApp.ViewActivated -= OnViewActivated;
        _uiApp.Application.DocumentChanged -= OnDocumentChanged;
    }

    private void CaptureInitialState()
    {
        try
        {
            var uiDoc = _uiApp.ActiveUIDocument;
            if (uiDoc is null) return;

            CaptureView(uiDoc.Document.ActiveView);
            CaptureSelection(uiDoc);
        }
        catch { }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            var uiDoc = _uiApp.ActiveUIDocument;
            if (uiDoc is null) return;

            CaptureSelection(uiDoc);
            _cache.LastSelectionChangeUtc = DateTime.UtcNow;
            _cache.NotifyChanged();
        }
        catch { }
    }

    private void OnViewActivated(object? sender, ViewActivatedEventArgs e)
    {
        try
        {
            CaptureView(e.CurrentActiveView);
            _cache.LastViewChangeUtc = DateTime.UtcNow;
            _cache.NotifyChanged();
        }
        catch { }
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        try
        {
            _cache.LastDocumentChange = new DocumentChangeDigest
            {
                ModifiedIds = e.GetModifiedElementIds().Select(id => id.Value).ToList(),
                AddedIds = e.GetAddedElementIds().Select(id => id.Value).ToList(),
                DeletedIds = e.GetDeletedElementIds().Select(id => id.Value).ToList()
            };
            _cache.LastDocumentChangeUtc = DateTime.UtcNow;
            _cache.NotifyChanged();
        }
        catch { }
    }

    private void CaptureView(View? view)
    {
        if (view is null) return;
        _cache.CurrentView = new ViewSnapshot
        {
            ViewId = view.Id.Value,
            ViewName = view.Name,
            ViewType = view.ViewType.ToString(),
            LevelName = view.GenLevel?.Name,
            Scale = view.Scale
        };
    }

    private void CaptureSelection(UIDocument uiDoc)
    {
        try
        {
            var selectedIds = uiDoc.Selection.GetElementIds();
            var doc = uiDoc.Document;

            _cache.CurrentSelection = new SelectionSnapshot
            {
                ElementIds = selectedIds.Select(id => id.Value).ToList(),
                Count = selectedIds.Count,
                Categories = selectedIds
                    .Select(id => doc.GetElement(id)?.Category?.Name)
                    .Where(c => c != null)
                    .Distinct()
                    .Cast<string>()
                    .ToList()
            };
        }
        catch { }
    }

    public void Dispose()
    {
        Unsubscribe();
        GC.SuppressFinalize(this);
    }
}
