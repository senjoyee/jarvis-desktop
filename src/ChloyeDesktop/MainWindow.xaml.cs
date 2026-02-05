using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ChloyeDesktop.Bridge;

namespace ChloyeDesktop;

public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    private readonly BridgeHandler _bridgeHandler;
    private bool _isDevMode;

    public MainWindow()
    {
        InitializeComponent();
        _logger = App.Services.GetRequiredService<ILogger<MainWindow>>();
        _bridgeHandler = new BridgeHandler(App.Services);
        
        // Wire up streaming event callback - use sync Invoke to prevent message batching
        _bridgeHandler.OnStreamEvent = (json) =>
        {
            webView.Dispatcher.Invoke(() =>
            {
                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.PostWebMessageAsString(json);
                }
            });
        };
        
        _isDevMode = Environment.GetEnvironmentVariable("CHLOYE_DEV_MODE") == "1" ||
                     System.Diagnostics.Debugger.IsAttached;

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await InitializeWebView();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize WebView2");
            MessageBox.Show($"Failed to initialize WebView2: {ex.Message}\n\nPlease ensure WebView2 Runtime is installed.",
                "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task InitializeWebView()
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChloyeDesktop", "WebView2");

        Directory.CreateDirectory(userDataFolder);

        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await webView.EnsureCoreWebView2Async(env);

        webView.CoreWebView2.Settings.IsScriptEnabled = true;
        webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
        webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
        webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

        webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

        string url;
        if (_isDevMode)
        {
            url = "http://localhost:5173";
        }
        else
        {
            // Use virtual host mapping to serve local files with proper security context
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var wwwrootPath = Path.Combine(appDir, "wwwroot");
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.local", 
                wwwrootPath, 
                CoreWebView2HostResourceAccessKind.Allow);
            url = "https://app.local/index.html";
        }

        _logger.LogInformation("Loading UI from: {Url}", url);
        webView.Source = new Uri(url);

        webView.CoreWebView2.NavigationCompleted += (s, args) =>
        {
            loadingText.Visibility = Visibility.Collapsed;
            if (!args.IsSuccess)
            {
                _logger.LogWarning("Navigation failed with error: {Error}", args.WebErrorStatus);
            }
        };
    }

    private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(message))
            {
                _logger.LogWarning("Received empty message");
                return;
            }
            _logger.LogDebug("Received message: {Message}", message);

            var response = await _bridgeHandler.HandleMessage(message);
            
            await webView.Dispatcher.InvokeAsync(() =>
            {
                webView.CoreWebView2.PostWebMessageAsString(response);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling web message");
        }
    }

    public void SendToUI(string json)
    {
        webView.Dispatcher.Invoke(() =>
        {
            webView.CoreWebView2?.PostWebMessageAsString(json);
        });
    }
}
