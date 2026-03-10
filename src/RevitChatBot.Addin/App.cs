using Autodesk.Revit.UI;

namespace RevitChatBot.Addin;

public class App : IExternalApplication
{
    public static App? Instance { get; private set; }
    public UIControlledApplication? UiApp { get; private set; }

    public Result OnStartup(UIControlledApplication application)
    {
        Instance = this;
        UiApp = application;

        CreateRibbonPanel(application);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        Instance = null;
        return Result.Succeeded;
    }

    private void CreateRibbonPanel(UIControlledApplication app)
    {
        var panel = app.CreateRibbonPanel("MEP ChatBot");

        var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

        var buttonData = new PushButtonData(
            "ShowChatBot",
            "MEP\nChatBot",
            assemblyPath,
            "RevitChatBot.Addin.Commands.ShowChatBotCommand");

        if (panel.AddItem(buttonData) is PushButton button)
        {
            button.ToolTip = "Open MEP ChatBot assistant powered by Ollama LLM";
            button.LongDescription = "An AI assistant that helps with MEP tasks in Revit. "
                + "Query elements, create/modify components, detect clashes, and more.";
        }
    }
}
