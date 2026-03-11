using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nice3point.Revit.Toolkit.External;
using Nice3point.Revit.Extensions;
using RevitChatBot.Addin.Commands;
using RevitChatBot.Addin.Handlers;

namespace RevitChatBot.Addin;

public class App : ExternalApplication
{
    public static App? Instance { get; private set; }
    public RevitEventHandler EventHandler { get; private set; } = null!;

    public override void OnStartup()
    {
        Instance = this;
        EventHandler = new RevitEventHandler();

        var panel = Application.CreatePanel("AI", "RevitChatBot");

        var button = panel.AddPushButton<ShowChatBotCommand>("Revit\nChatBot");
        button.ToolTip = "Open RevitChatBot - AI assistant for Revit MEP";
        button.LongDescription =
            "An AI-powered assistant that helps with MEP tasks in Revit.\n" +
            "Query elements, create/modify components, detect clashes, " +
            "analyze systems, and more.\nPowered by Ollama LLM.";
        button.LargeImage = CreateButtonIcon(32);
        button.Image = CreateButtonIcon(16);
    }

    public override void OnShutdown()
    {
        Instance = null;
    }

    private static BitmapSource CreateButtonIcon(int size)
    {
        var dpi = 96;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bgBrush = new LinearGradientBrush(
                Color.FromRgb(59, 130, 246),
                Color.FromRgb(99, 102, 241),
                45);
            dc.DrawEllipse(bgBrush, null, new Point(size / 2.0, size / 2.0),
                size / 2.0, size / 2.0);

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
