using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using RevitChatBot.Addin.Handlers;
using RevitChatBot.Addin.Views;
using RevitChatBot.Core.Context;

namespace RevitChatBot.Addin.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class ShowChatBotCommand : ExternalCommand
{
    private static ChatBotWindow? _window;
    private static RevitContextEventHooks? _contextHooks;

    public override void Execute()
    {
        var eventHandler = App.Instance?.EventHandler;

        if (eventHandler is null)
        {
            Result = Autodesk.Revit.UI.Result.Failed;
            ErrorMessage = "RevitChatBot not initialized.";
            return;
        }

        if (_window is { IsLoaded: true })
        {
            _window.Activate();
            return;
        }

        var contextCache = new RevitContextCache();
        _contextHooks?.Dispose();
        _contextHooks = new RevitContextEventHooks(UiApplication, contextCache);
        _contextHooks.Subscribe();

        var initData = new BridgeInitData
        {
            Document = Document,
            DocumentTitle = Document?.Title,
            ContextCache = contextCache,
            ContextHooks = _contextHooks
        };

        _window = new ChatBotWindow(eventHandler, initData);
        _window.Show();
    }
}

public class BridgeInitData
{
    public Autodesk.Revit.DB.Document? Document { get; set; }
    public string? DocumentTitle { get; set; }
    public RevitContextCache ContextCache { get; set; } = new();
    public RevitContextEventHooks? ContextHooks { get; set; }
}
