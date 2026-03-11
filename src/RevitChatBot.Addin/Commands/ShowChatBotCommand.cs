using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitChatBot.Addin.Handlers;
using RevitChatBot.Addin.Views;
using RevitChatBot.Core.Context;

namespace RevitChatBot.Addin.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class ShowChatBotCommand : IExternalCommand
{
    private static ChatBotWindow? _window;
    private static RevitContextEventHooks? _contextHooks;

    public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
    {
        try
        {
            var uiApp = commandData.Application;
            var eventHandler = App.Instance?.EventHandler;

            if (eventHandler is null)
            {
                message = "RevitChatBot not initialized.";
                return Result.Failed;
            }

            if (_window is { IsLoaded: true })
            {
                _window.Activate();
                return Result.Succeeded;
            }

            var doc = uiApp.ActiveUIDocument?.Document;

            var contextCache = new RevitContextCache();
            _contextHooks?.Dispose();
            _contextHooks = new RevitContextEventHooks(uiApp, contextCache);
            _contextHooks.Subscribe();

            var initData = new BridgeInitData
            {
                Document = doc,
                DocumentTitle = doc?.Title,
                ContextCache = contextCache,
                ContextHooks = _contextHooks
            };

            _window = new ChatBotWindow(eventHandler, initData);
            _window.Show();

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}

public class BridgeInitData
{
    public Document? Document { get; set; }
    public string? DocumentTitle { get; set; }
    public RevitContextCache ContextCache { get; set; } = new();
    public RevitContextEventHooks? ContextHooks { get; set; }
}
