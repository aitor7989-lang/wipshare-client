using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using WipShare.Client.Config;

namespace WipShare.Client.Setup;

/// <summary>
/// The About window: WipShare mark, the real assembly version, the tagline, a
/// working website link, a "Your clips" link to the /me library, and a static
/// credits line. No update checker (there's no updater, so a hardcoded
/// "Up to date" would be misleading).
/// </summary>
public partial class AboutWindow : Window
{
    private readonly ILogger _logger;

    public AboutWindow(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<AboutWindow>();
        InitializeComponent();

        var v = typeof(AboutWindow).Assembly.GetName().Version;
        VersionText.Text = v is null ? "-" : $"{v.Major}.{v.Minor}.{v.Build}";

        var host = new Uri(Constants.UploadBaseUrl).Host;
        WebsiteText.Text = host;
        YourClipsText.Text = $"{host}/me";

        // Clip content to the chrome's rounded corners so the close button's red
        // hover never spills past the corner (design overflow:hidden).
        ContentRoot.SizeChanged += (_, _) => UpdateCornerClip();
        UpdateCornerClip();

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private void UpdateCornerClip() =>
        ContentRoot.Clip = new System.Windows.Media.RectangleGeometry(
            new Rect(0, 0, ContentRoot.ActualWidth, ContentRoot.ActualHeight), 7, 7);

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

    private void OnYourClipsClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = $"{Constants.UploadBaseUrl.TrimEnd('/')}/me", UseShellExecute = true }); }
        catch (Exception ex) { _logger.LogError(ex, "Opening your-clips failed"); }
    }
}
