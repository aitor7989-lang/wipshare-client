using Microsoft.Win32;

namespace WipShare.Client.Settings;

/// <summary>
/// Launch-at-startup, backed by the HKCU Run key - the OS is the single source of
/// truth (so the Settings toggle reflects reality even if changed elsewhere). All
/// methods are best-effort and never throw.
/// </summary>
internal static class StartupRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WipShare";

    /// <summary>True if a Run entry for WipShare currently exists.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Adds or removes the Run entry (quoted exe path). Returns false on failure.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return false;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
