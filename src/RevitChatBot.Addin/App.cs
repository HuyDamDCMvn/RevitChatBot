using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using RevitChatBot.Addin.Handlers;

namespace RevitChatBot.Addin;

public class App : IExternalApplication
{
    public static App? Instance { get; private set; }
    public UIControlledApplication? UiApp { get; private set; }
    public RevitEventHandler EventHandler { get; private set; } = null!;

    public Result OnStartup(UIControlledApplication application)
    {
        Instance = this;
        UiApp = application;

        EventHandler = new RevitEventHandler();
        EventHandler.CreateExternalEvent();

        CreateRibbonPanel(application);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        EventHandler?.Unregister();
        Instance = null;
        return Result.Succeeded;
    }

    private void CreateRibbonPanel(UIControlledApplication app)
    {
        var panel = app.CreateRibbonPanel("AI");

        var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

        var buttonData = new PushButtonData(
            "ShowChatBot",
            "Revit\nChatBot",
            assemblyPath,
            "RevitChatBot.Addin.Commands.ShowChatBotCommand");

        if (panel.AddItem(buttonData) is PushButton button)
        {
            button.ToolTip = "Open RevitChatBot - AI assistant for Revit MEP";
            button.LongDescription =
                "An AI-powered assistant that helps with MEP tasks in Revit.\n" +
                "Query elements, create/modify components, detect clashes, " +
                "analyze systems, and more.\nPowered by Ollama LLM.";
            button.LargeImage = CreateButtonIcon(32);
            button.Image = CreateButtonIcon(16);
        }
    }

    private static BitmapSource CreateButtonIcon(int size)
    {
        var dpi = 96;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, size, size);

            // Background circle
            var bgBrush = new LinearGradientBrush(
                Color.FromRgb(59, 130, 246),   // blue-500
                Color.FromRgb(99, 102, 241),   // indigo-500
                45);
            dc.DrawEllipse(bgBrush, null, new Point(size / 2.0, size / 2.0),
                size / 2.0, size / 2.0);

            // "AI" text
            var fontSize = size * 0.42;
            var typeface = new Typeface(
                new FontFamily("Segoe UI"),
                FontStyles.Normal,
                FontWeights.Bold,
                FontStretches.Normal);
            var text = new FormattedText("AI",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.White,
                dpi);
            dc.DrawText(text,
                new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        var bmp = new RenderTargetBitmap(size, size, dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }
}
