using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitChatBot.Addin.Views;

namespace RevitChatBot.Addin.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class ShowChatBotCommand : IExternalCommand
{
    private static ChatBotWindow? _window;

    public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
    {
        try
        {
            var uiApp = commandData.Application;

            if (_window is { IsLoaded: true })
            {
                _window.Activate();
                return Result.Succeeded;
            }

            _window = new ChatBotWindow(uiApp);
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
