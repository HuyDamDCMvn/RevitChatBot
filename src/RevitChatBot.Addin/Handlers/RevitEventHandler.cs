using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitChatBot.Addin.Handlers;

/// <summary>
/// Bridges async code to Revit's main thread via IExternalEventHandler.
/// Revit API calls must run on the main thread; this handler queues
/// work items and executes them when Revit raises the external event.
/// </summary>
public class RevitEventHandler : IExternalEventHandler
{
    private ExternalEvent? _externalEvent;
    private Func<Document, object?>? _pendingAction;
    private TaskCompletionSource<object?>? _tcs;

    public void Register(UIApplication uiApp)
    {
        _externalEvent = ExternalEvent.Create(this);
    }

    public void Unregister()
    {
        _externalEvent?.Dispose();
        _externalEvent = null;
    }

    public Task<object?> ExecuteAsync(Func<Document, object?> action)
    {
        if (_externalEvent is null)
            throw new InvalidOperationException("Handler not registered.");

        _tcs = new TaskCompletionSource<object?>();
        _pendingAction = action;
        _externalEvent.Raise();

        return _tcs.Task;
    }

    public void Execute(UIApplication app)
    {
        var tcs = _tcs;
        var action = _pendingAction;
        _pendingAction = null;
        _tcs = null;

        if (tcs is null || action is null) return;

        try
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc is null)
            {
                tcs.SetException(new InvalidOperationException("No active document."));
                return;
            }

            var result = action(doc);
            tcs.SetResult(result);
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
        }
    }

    public string GetName() => "RevitChatBot.EventHandler";
}
