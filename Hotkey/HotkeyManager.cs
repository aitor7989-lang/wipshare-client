using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NHotkey;
using WipShare.Client.Settings;
using NHotkeyManager = NHotkey.Wpf.HotkeyManager;

namespace WipShare.Client.Hotkey;

/// <summary>
/// Thin wrapper around NHotkey.Wpf's process-wide hotkey registry. Owns the registration
/// for our single Ctrl+Shift+R binding and re-raises the press as a clean .NET event.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly ILogger<HotkeyManager> _logger;
    private bool _registered;
    private bool _disposed;

    /// <summary>Raised on the WPF UI thread when the hotkey fires.</summary>
    public event EventHandler? HotkeyPressed;

    public HotkeyManager(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<HotkeyManager>();
    }

    public void Register()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HotkeyManager));
        if (_registered) return;

        try
        {
            NHotkeyManager.Current.AddOrReplace(
                AppSettings.HotkeyName,
                Key.R,
                ModifierKeys.Control | ModifierKeys.Shift,
                OnHotkey);
            _registered = true;
            _logger.LogInformation("Registered global hotkey Ctrl+Shift+R as '{Name}'", AppSettings.HotkeyName);
        }
        catch (HotkeyAlreadyRegisteredException ex)
        {
            _logger.LogError(ex, "Failed to register Ctrl+Shift+R: already claimed by another process");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected failure registering hotkey");
            throw;
        }
    }

    private void OnHotkey(object? sender, HotkeyEventArgs e)
    {
        e.Handled = true;
        _logger.LogInformation("Hotkey '{Name}' pressed", e.Name);

        try
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subscriber threw from HotkeyPressed handler");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_registered)
        {
            try
            {
                NHotkeyManager.Current.Remove(AppSettings.HotkeyName);
                _logger.LogInformation("Unregistered global hotkey '{Name}'", AppSettings.HotkeyName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unregister hotkey '{Name}'", AppSettings.HotkeyName);
            }
        }
    }
}
