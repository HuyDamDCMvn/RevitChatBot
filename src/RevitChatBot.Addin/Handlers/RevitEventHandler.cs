using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitChatBot.Addin.Handlers;

/// <summary>
/// Bridges async code to Revit's main thread via IExternalEventHandler.
/// Uses a SemaphoreSlim to serialize calls - only one action can be pending at a time.
/// </summary>
public class RevitEventHandler : IExternalEventHandler
{
    private ExternalEvent? _externalEvent;
    private Func<Document, object?>? _pendingAction;
    private TaskCompletionSource<object?>? _tcs;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void CreateExternalEvent()
    {
        _externalEvent = ExternalEvent.Create(this);
    }

    public void Unregister()
    {
        _externalEvent?.Dispose();
        _externalEvent = null;
    }

    public async Task<object?> ExecuteAsync(Func<Document, object?> action)
    {
        if (_externalEvent is null)
            throw new InvalidOperationException("Handler not registered.");

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var tcs = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _tcs = tcs;
            _pendingAction = action;
            _externalEvent.Raise();

            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
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
                tcs.TrySetException(new InvalidOperationException("No active document."));
                return;
            }

            var result = action(doc);
            tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    }

    public string GetName() => "RevitChatBot.EventHandler";
}
