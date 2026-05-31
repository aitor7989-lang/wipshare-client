using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;

namespace WipShare.Client.Tray;

public sealed class TrayApp : IDisposable
{
    private const string DefaultTooltip = "WipShare";

    private readonly ILogger<TrayApp> _logger;
    private TaskbarIcon? _trayIcon;
    private MenuItem? _statusItem;
    private MenuItem? _openLastLinkItem;
    private string? _lastLink;
    private bool _disposed;

    /// <summary>Raised when the user picks "Retry failed uploads".</summary>
    public event EventHandler? RetryFailedRequested;

    /// <summary>Raised when the user picks "Change invite code".</summary>
    public event EventHandler? ChangeInviteCodeRequested;

    public TrayApp(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<TrayApp>();
    }

    public void Initialize()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = DefaultTooltip,
            Icon = LoadTrayIcon(),
        };

        var menu = new ContextMenu();

        _statusItem = new MenuItem { Header = "WipShare: idle", IsEnabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Separator());

        _openLastLinkItem = MakeMenuItem("Open last link", OnOpenLastLink);
        _openLastLinkItem.IsEnabled = false;
        menu.Items.Add(_openLastLinkItem);
        menu.Items.Add(MakeMenuItem("Retry failed uploads", OnRetryFailed));
        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem("Change invite code…", OnChangeInviteCode));
        menu.Items.Add(MakeMenuItem("Settings", OnSettings));
        menu.Items.Add(MakeMenuItem("About", OnAbout));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Quit", OnQuit));
        _trayIcon.ContextMenu = menu;

        _logger.LogInformation("Tray icon initialized");
    }

    /// <summary>Updates the upload badge/tooltip. Safe to call from any thread.</summary>
    public void SetUploadStatus(int activeCount)
    {
        RunOnUi(() =>
        {
            if (_statusItem != null)
                _statusItem.Header = activeCount > 0 ? $"Uploading {activeCount}…" : "WipShare: idle";
            if (_trayIcon != null)
                _trayIcon.ToolTipText = activeCount > 0 ? $"{DefaultTooltip} — uploading {activeCount}…" : DefaultTooltip;
        });
    }

    /// <summary>Records the most recent shareable link and enables "Open last link". Any thread.</summary>
    public void SetLastLink(string url)
    {
        RunOnUi(() =>
        {
            _lastLink = url;
            if (_openLastLinkItem != null) _openLastLinkItem.IsEnabled = true;
        });
    }

    public void ShowBalloon(string title, string message, BalloonIcon icon = BalloonIcon.Info)
    {
        var tray = _trayIcon;
        if (tray == null) return;
        if (Application.Current?.Dispatcher.CheckAccess() == true)
            tray.ShowBalloonTip(title, message, icon);
        else
            Application.Current?.Dispatcher.BeginInvoke(() => tray.ShowBalloonTip(title, message, icon));
    }

    private static MenuItem MakeMenuItem(string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    private void OnSettings(object? sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Settings UI not implemented in Phase 1.",
            "WipShare",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnAbout(object? sender, RoutedEventArgs e)
    {
        var version = typeof(TrayApp).Assembly.GetName().Version?.ToString() ?? "unknown";
        MessageBox.Show(
            $"WipShare Client – Phase 1\nVersion {version}\n\nWindow recording for 3D artists.",
            "About WipShare",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnQuit(object? sender, RoutedEventArgs e)
    {
        _logger.LogInformation("Quit selected from tray menu");
        Application.Current.Shutdown();
    }

    private void OnOpenLastLink(object? sender, RoutedEventArgs e)
    {
        var url = _lastLink;
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            // Our own viewer URL — open in the default browser.
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open last link");
        }
    }

    private void OnRetryFailed(object? sender, RoutedEventArgs e)
    {
        _logger.LogInformation("Retry failed uploads selected from tray menu");
        RetryFailedRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnChangeInviteCode(object? sender, RoutedEventArgs e)
    {
        _logger.LogInformation("Change invite code selected from tray menu");
        ChangeInviteCodeRequested?.Invoke(this, EventArgs.Empty);
    }

    private static void RunOnUi(Action action)
    {
        var app = Application.Current;
        if (app is null) { action(); return; }
        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.BeginInvoke(action);
    }

    private static Icon LoadTrayIcon()
    {
        var uri = new Uri("pack://application:,,,/Tray/TrayIcon.ico", UriKind.Absolute);
        var resource = Application.GetResourceStream(uri)
            ?? throw new InvalidOperationException("Embedded TrayIcon.ico resource not found");
        using var stream = resource.Stream;
        return new Icon(stream);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
