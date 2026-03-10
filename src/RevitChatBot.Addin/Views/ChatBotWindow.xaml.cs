using System.IO;
using System.Windows;
using Autodesk.Revit.UI;
using Microsoft.Web.WebView2.Core;
using RevitChatBot.Addin.Bridge;

namespace RevitChatBot.Addin.Views;

public partial class ChatBotWindow : Window
{
    private readonly UIApplication _uiApp;
    private WebViewBridge? _bridge;

    public ChatBotWindow(UIApplication uiApp)
    {
        _uiApp = uiApp;
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(
                    Path.GetTempPath(), "RevitChatBot_WebView2"));

            await WebView.EnsureCoreWebView2Async(env);

            _bridge = new WebViewBridge(WebView, _uiApp);
            _bridge.Initialize();

            var uiPath = FindUiPath();
            if (uiPath is not null)
            {
                WebView.CoreWebView2.Navigate(new Uri(uiPath).AbsoluteUri);
            }
            else
            {
                WebView.CoreWebView2.NavigateToString(GetFallbackHtml());
            }

            LoadingText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            LoadingText.Text = $"Error loading WebView2: {ex.Message}";
        }
    }

    private static string? FindUiPath()
    {
        var assemblyDir = Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

        var candidates = new[]
        {
            Path.Combine(assemblyDir, "ui", "index.html"),
            Path.Combine(assemblyDir, "..", "ui", "revitchatbot-ui", "dist", "index.html"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string GetFallbackHtml()
    {
        return """
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8" /></head>
            <body style="font-family:sans-serif;padding:40px;text-align:center;color:#666;">
                <h2>MEP ChatBot</h2>
                <p>UI not found. Please build the React UI first:</p>
                <code>cd ui/revitchatbot-ui && npm install && npm run build</code>
            </body>
            </html>
            """;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _bridge?.Dispose();
        WebView?.Dispose();
    }
}
