using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.AspNetCore.Builder;
using Microsoft.Web.WebView2.Core;
using Sntp.Adept.Api;

namespace Sntp.Adept.Host;

public partial class MainWindow : Window
{
    private WebApplication? _api;
    private CoreWebView2Environment? _env;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
        Closed += (_, _) =>
        {
            try { _api?.StopAsync().GetAwaiter().GetResult(); } catch { /* ignore */ }
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var port = FindFreePort(5179);
            var content = FindContentRoot();
            _api = await Task.Run(() => AdeptWebApp.Build([], content, port));
            await _api.StartAsync();
            var url = _api.Urls.FirstOrDefault() ?? $"http://127.0.0.1:{port}";
            StatusText.Text = url;

            _env = await CoreWebView2Environment.CreateAsync();
            await WebView.EnsureCoreWebView2Async(_env);
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            WebView.CoreWebView2.NewWindowRequested += OnNewWindow;
            WebView.Source = new Uri(url);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(ex.ToString(), "SNTP-ADEPT", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (_env is not null
            && !string.IsNullOrWhiteSpace(e.Uri)
            && e.Uri.Contains("preview=1", StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
            new PreviewWindow(_env, e.Uri).Show();
            return;
        }
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.Host is not "127.0.0.1" and not "localhost")
        {
            e.Handled = true;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (ctrl && e.Key == Key.V)
        {
            e.Handled = true;
            if (WebView.CoreWebView2 is null) return;
            var text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            var html = Clipboard.ContainsText(TextDataFormat.Html) ? Clipboard.GetText(TextDataFormat.Html) : "";
            var payload = System.Text.Json.JsonSerializer.Serialize(new { cmd = "paste", text, html });
            WebView.CoreWebView2.PostWebMessageAsString(payload);
            return;
        }
        string? cmd = null;
        if (ctrl && e.Key == Key.E) cmd = "edit";
        else if (ctrl && e.Key == Key.R) cmd = "factors";
        else if (ctrl && e.Key == Key.Z) cmd = "undo";
        else if (e.Key == Key.Delete) cmd = "delete";
        if (cmd is null) return;
        e.Handled = true;
        if (WebView.CoreWebView2 is not null)
            _ = WebView.ExecuteScriptAsync($"window.sntpHotkey && window.sntpHotkey('{cmd}')");
    }

    private static string FindContentRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            baseDir,
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Sntp.Adept.Api")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "Sntp.Adept.Api")),
        };
        foreach (var c in candidates)
        {
            if (Directory.Exists(Path.Combine(c, "wwwroot")))
                return c;
        }
        return baseDir;
    }

    private static int FindFreePort(int preferred)
    {
        try
        {
            var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, preferred);
            l.Start();
            l.Stop();
            return preferred;
        }
        catch
        {
            var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            l.Start();
            var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }
    }
}
