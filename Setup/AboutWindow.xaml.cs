using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using WipShare.Client.Config;

namespace WipShare.Client.Setup;

/// <summary>
/// The About window: WipShare mark, the real assembly version, the tagline, a
/// working website link, and a static credits line. No update checker and no
/// "Your clips" link (those surfaces don't exist).
/// </summary>
public partial class AboutWindow : Window
{
    private readonly ILogger _logger;

    public AboutWindow(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<AboutWindow>();
        InitializeComponent();

        var v = typeof(AboutWindow).Assembly.GetName().Version;
        VersionText.Text = v is null ? "—" : $"{v.Major}.{v.Minor}.{v.Build}";

        var host = new Uri(Constants.UploadBaseUrl).Host;
        WebsiteText.Text = host;

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) { try { DragMove(); } catch { /* ignore */ } }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnWebsiteClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = Constants.UploadBaseUrl, UseShellExecute = true }); }
        catch (Exception ex) { _logger.LogError(ex, "Opening website failed"); }
    }
}
