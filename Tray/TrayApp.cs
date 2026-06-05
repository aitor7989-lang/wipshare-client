using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;

namespace WipShare.Client.Tray;

/// <summary>
/// The tray icon, its tooltip, and the context menu. Drives a small state machine
/// (idle / recording / uploading / failed / not-set-up) off capture + upload
/// events, swapping a composed icon and tooltip per state. The "link copied"
/// moment is owned by the toast system — never duplicated here.
/// </summary>
public sealed class TrayApp : IDisposable
{
    private readonly ILogger<TrayApp> _logger;
    private TaskbarIcon? _trayIcon;
    private readonly Dictionary<TrayState, Icon> _icons = new();

    private MenuItem? _statusItem;
    private MenuItem? _openLastLinkItem;
    private MenuItem? _retryItem;
    private string? _lastLink;

    // state inputs
    private bool _configured;
    private bool _recording;
    private int _uploadingCount;
    private int _failedCount;

    private bool _disposed;

    public event EventHandler? RetryFailedRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? AboutRequested;

    public TrayApp(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<TrayApp>();
    }

    public void Initialize()
    {
        foreach (TrayState s in Enum.GetValues<TrayState>())
            _icons[s] = TrayIconFactory.Create(s);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "WipShare — Ready",
            Icon = _icons[TrayState.Idle],
        };

        var menu = new ContextMenu();
        _statusItem = new MenuItem { Header = "Ready", IsEnabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Separator());

        _openLastLinkItem = MakeMenuItem("Open last link", OnOpenLastLink);
        _openLastLinkItem.IsEnabled = false;
        menu.Items.Add(_openLastLinkItem);

        _retryItem = MakeMenuItem("Retry failed uploads", OnRetryFailed);
        _retryItem.IsEnabled = false;
        menu.Items.Add(_retryItem);
        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem("Settings…", OnSettings));
        menu.Items.Add(MakeMenuItem("About WipShare", OnAbout));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Quit WipShare", OnQuit));
        _trayIcon.ContextMenu = menu;

        _logger.LogInformation("Tray icon initialized");
    }

    // ----- state inputs (any thread) -----

    /// <summary>Whether an invite code is present (sets the Idle vs Not-set-up base state).</summary>
    public void SetConfigured(bool configured) { _configured = configured; Recompute(); }

    /// <summary>Whether a capture is actively recording.</summary>
    public void SetRecording(bool recording) { _recording = recording; Recompute(); }

    /// <summary>Queued + in-flight upload count (the existing "Uploading N…" signal).</summary>
    public void SetUploadStatus(int activeCount) { _uploadingCount = activeCount; Recompute(); }

    /// <summary>Number of uploads currently in the failed-pending state.</summary>
    public void SetFailedCount(int failedCount) { _failedCount = failedCount; Recompute(); }

    private void Recompute()
    {
        RunOnUi(() =>
        {
            if (_trayIcon == null) return;

            TrayState state;
            string tooltip, status;
            if (_recording)
            {
                state = TrayState.Recording; tooltip = "WipShare — Recording…"; status = "Recording…";
            }
            else if (_uploadingCount > 0)
            {
                state = TrayState.Uploading;
                tooltip = $"WipShare — Uploading {_uploadingCount}…";
                status = $"Uploading {_uploadingCount}…";
            }
            else if (_failedCount > 0)
            {
                state = TrayState.Failed;
                tooltip = "Upload failed — click to retry";
                status = _failedCount == 1 ? "1 upload failed" : $"{_failedCount} uploads failed";
            }
            else if (!_configured)
            {
                state = TrayState.NotSetUp; tooltip = "Local only — add an invite code to share"; status = "Local only";
            }
            else
            {
                state = TrayState.Idle; tooltip = "WipShare — Ready"; status = "Ready";
            }

            _trayIcon.ToolTipText = tooltip;
            if (_icons.TryGetValue(state, out var icon)) _trayIcon.Icon = icon;
            if (_statusItem != null) _statusItem.Header = status;
            if (_retryItem != null) _retryItem.IsEnabled = _failedCount > 0;
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

    // ----- menu -----

    private static MenuItem MakeMenuItem(string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    private void OnSettings(object? sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private void OnAbout(object? sender, RoutedEventArgs e) => AboutRequested?.Invoke(this, EventArgs.Empty);

    private void OnQuit(object? sender, RoutedEventArgs e)
    {
        _logger.LogInformation("Quit selected from tray menu");
        Application.Current.Shutdown();
    }

    private void OnOpenLastLink(object? sender, RoutedEventArgs e)
    {
        var url = _lastLink;
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to open last link"); }
    }

    private void OnRetryFailed(object? sender, RoutedEventArgs e)
    {
        _logger.LogInformation("Retry failed uploads selected from tray menu");
        RetryFailedRequested?.Invoke(this, EventArgs.Empty);
    }

    private static void RunOnUi(Action action)
    {
        var app = Application.Current;
        if (app is null) { action(); return; }
        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
        foreach (var icon in _icons.Values)
        {
            try { icon.Dispose(); } catch { /* ignore */ }
        }
        _icons.Clear();
    }
}
