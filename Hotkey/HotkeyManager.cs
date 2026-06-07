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

        var mods = AppSettings.HotkeyModifiers;
        var key = AppSettings.HotkeyKey;
        try
        {
            NHotkeyManager.Current.AddOrReplace(AppSettings.HotkeyName, key, mods, OnHotkey);
            _registered = true;
            _logger.LogInformation("Registered global hotkey {Combo} as '{Name}'", Describe(mods, key), AppSettings.HotkeyName);
        }
        catch (HotkeyAlreadyRegisteredException ex)
        {
            _logger.LogError(ex, "Failed to register {Combo}: already claimed by another process", Describe(mods, key));
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected failure registering hotkey");
            throw;
        }
    }

    /// <summary>
    /// Re-registers the global hotkey to a new combo. The OS is the source of
    /// truth: if RegisterHotKey fails (another app owns the combo), the PREVIOUS
    /// combo is restored so the app is never left without a working hotkey, and
    /// this returns false. On success the new combo is persisted and true returned.
    /// Call on the UI thread.
    /// </summary>
    public bool TryReRegister(ModifierKeys newMods, Key newKey)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HotkeyManager));

        var oldMods = AppSettings.HotkeyModifiers;
        var oldKey = AppSettings.HotkeyKey;

        try
        {
            // AddOrReplace unregisters the old id and registers the new - if the
            // new one is taken it throws, having dropped the old.
            NHotkeyManager.Current.AddOrReplace(AppSettings.HotkeyName, newKey, newMods, OnHotkey);
            _registered = true;
            AppSettings.HotkeyModifiers = newMods;
            AppSettings.HotkeyKey = newKey;
            _logger.LogInformation("Re-registered global hotkey to {Combo}", Describe(newMods, newKey));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not register {Combo}; rolling back to {Old}",
                Describe(newMods, newKey), Describe(oldMods, oldKey));
            try
            {
                NHotkeyManager.Current.AddOrReplace(AppSettings.HotkeyName, oldKey, oldMods, OnHotkey);
                _registered = true;
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Rollback to previous hotkey {Old} also failed", Describe(oldMods, oldKey));
                _registered = false;
            }
            return false;
        }
    }

    /// <summary>Human-readable combo, e.g. "Ctrl + Shift + R".</summary>
    public static string Describe(ModifierKeys mods, Key key)
    {
        var parts = new List<string>(4);
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(key));
        return string.Join(" + ", parts);
    }

    /// <summary>Friendly single-key label (R, F9, Space, …).</summary>
    public static string KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => key.ToString()[1..],     // D5 → "5"
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + key.ToString()[6..],
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.Space => "Space",
        Key.Oem3 => "`",
        _ => key.ToString(),
    };

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
