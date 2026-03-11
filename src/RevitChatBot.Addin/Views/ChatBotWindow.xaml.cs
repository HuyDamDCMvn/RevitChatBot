using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using RevitChatBot.Addin.Bridge;
using RevitChatBot.Addin.Commands;
using RevitChatBot.Addin.Handlers;

namespace RevitChatBot.Addin.Views;

public partial class ChatBotWindow : Window
{
    private readonly RevitEventHandler _eventHandler;
    private readonly BridgeInitData _initData;
    private WebViewBridge? _bridge;

    public ChatBotWindow(RevitEventHandler eventHandler, BridgeInitData initData)
    {
        _eventHandler = eventHandler;
        _initData = initData;
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

            WebView.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                    LoadingText.Text = $"Navigation failed: {args.WebErrorStatus}";
            };

            _bridge = new WebViewBridge(WebView, _eventHandler, _initData);
            _bridge.Initialize();

            var uiFolder = FindUiFolder();
            if (uiFolder is not null)
            {
                WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "revitchatbot.local", uiFolder,
                    CoreWebView2HostResourceAccessKind.Allow);
                WebView.CoreWebView2.Navigate("https://revitchatbot.local/index.html");
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

    private static string? FindUiFolder()
    {
        var assemblyDir = Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

        var candidates = new[]
        {
            Path.Combine(assemblyDir, "ui"),
            Path.Combine(assemblyDir, "..", "ui", "revitchatbot-ui", "dist"),
        };

        return candidates.FirstOrDefault(d =>
            Directory.Exists(d) && File.Exists(Path.Combine(d, "index.html")));
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
