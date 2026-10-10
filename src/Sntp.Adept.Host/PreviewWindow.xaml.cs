using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Sntp.Adept.Host;

public partial class PreviewWindow : Window
{
    private readonly CoreWebView2Environment _env;
    private readonly string _url;

    public PreviewWindow(CoreWebView2Environment env, string url)
    {
        InitializeComponent();
        _env = env;
        _url = url;
        Loaded += OnLoaded;
        PlaceOnAnotherMonitor();
    }

    private void PlaceOnAnotherMonitor()
    {
        if (SystemParameters.VirtualScreenWidth <= SystemParameters.PrimaryScreenWidth + 80) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var workLeft = SystemParameters.WorkArea.Left;
        Left = SystemParameters.VirtualScreenLeft < workLeft - 40
            ? SystemParameters.VirtualScreenLeft
            : SystemParameters.WorkArea.Right;
        Top = SystemParameters.VirtualScreenTop + 24;
        Width = Math.Min(1200, Math.Max(860, SystemParameters.VirtualScreenWidth - SystemParameters.PrimaryScreenWidth - 48));
        Height = Math.Min(900, SystemParameters.VirtualScreenHeight - 64);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await WebView.EnsureCoreWebView2Async(_env);
        WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
        WebView.Source = new Uri(_url);
    }
}
