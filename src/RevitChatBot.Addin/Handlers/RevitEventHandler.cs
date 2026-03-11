using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External.Handlers;

namespace RevitChatBot.Addin.Handlers;

/// <summary>
/// Bridges async code to Revit's main thread via Nice3point AsyncEventHandler.
/// Wraps the toolkit handler to keep the existing Func&lt;Document, object?&gt; contract
/// used throughout Skills and WebViewBridge.
/// </summary>
public class RevitEventHandler
{
    private readonly AsyncEventHandler<object?> _handler = new();

    public async Task<object?> ExecuteAsync(Func<Document, object?> action, int timeoutMs = 30_000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);

        var task = _handler.RaiseAsync(app =>
        {
            var doc = app.ActiveUIDocument?.Document
                      ?? throw new InvalidOperationException("No active document.");
            return action(doc);
        });

        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs, cts.Token)).ConfigureAwait(false);
        if (completed != task)
            throw new TimeoutException("Timed out waiting for Revit operation to complete.");

        return await task.ConfigureAwait(false);
    }
}
