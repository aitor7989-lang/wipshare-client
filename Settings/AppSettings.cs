using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace WipShare.Client.Settings;

/// <summary>
/// App configuration: compile-time constants (not user-facing) plus a small set
/// of persisted user settings backed by <c>%LOCALAPPDATA%\WipShare\settings.json</c>.
/// Persisted properties apply immediately and write through on change; reads fall
/// back to sensible defaults if the file is missing or corrupt.
///
/// (Launch-at-startup is intentionally NOT stored here — the HKCU Run registry
/// value is its single source of truth; see Settings/StartupRegistry.cs.)
/// </summary>
public static class AppSettings
{
    // ===== compile-time constants (not user-editable) =====
    public const int RecordDurationSeconds = 15;
    public const int TargetFps = 30;
    public const int MaxWidth = 1920;
    public const int MaxHeight = 1080;
    public const uint TargetBitrateBps = 8_000_000;

    // Region capture
    public const int MinRegionWidth  = 50;
    public const int MinRegionHeight = 50;
    public const double BackdropOpacity = 0.4;

    public const string HotkeyName = "WipShareToggleRecord";

    // Upload is always attempted when an invite code is present (not a user toggle).
    public const bool AutoUpload = true;

    /// <summary>Index of the captured frame retained as the poster thumbnail (~1s at 30 fps).</summary>
    public const int PosterFrameIndex = 30;
    /// <summary>Longest-side cap (px) for the generated JPEG thumbnail.</summary>
    public const int ThumbnailMaxEdge = 1280;

    // ===== paths =====
    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WipShare");

    public static string LogFilePath => Path.Combine(LogDirectory, "log.txt");
    public static string ConfigFilePath => Path.Combine(LogDirectory, "config.json");

    /// <summary>Per-device identity (owner_token). Not a secret — plain JSON.</summary>
    public static string IdentityFilePath => Path.Combine(LogDirectory, "identity.json");

    /// <summary>Persisted user settings file.</summary>
    public static string SettingsFilePath => Path.Combine(LogDirectory, "settings.json");

    /// <summary>The out-of-the-box output location (used when no folder has been chosen).</summary>
    public static string DefaultOutputDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", "WipShare");

    // ===== persisted user settings =====
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };
    private static Persisted _current = Persisted.Load();

    /// <summary>The recording shortcut's modifier keys (default Ctrl + Shift).</summary>
    public static ModifierKeys HotkeyModifiers
    {
        get => (ModifierKeys)_current.HotkeyModifiers;
        set { _current.HotkeyModifiers = (int)value; Save(); }
    }

    /// <summary>The recording shortcut's main key (default R).</summary>
    public static Key HotkeyKey
    {
        get => (Key)_current.HotkeyKey;
        set { _current.HotkeyKey = (int)value; Save(); }
    }

    /// <summary>When false, the 3-2-1 countdown is skipped and recording starts immediately.</summary>
    public static bool CountdownEnabled
    {
        get => _current.CountdownEnabled;
        set { _current.CountdownEnabled = value; Save(); }
    }

    /// <summary>Where local .mp4 copies are written (falls back to the default location).</summary>
    public static string OutputFolder
    {
        get => string.IsNullOrWhiteSpace(_current.OutputFolder) ? DefaultOutputDirectory : _current.OutputFolder!;
        set { _current.OutputFolder = value; Save(); }
    }

    /// <summary>When true, a completed upload copies the share link to the clipboard.</summary>
    public static bool AutoCopyLink
    {
        get => _current.AutoCopyLink;
        set { _current.AutoCopyLink = value; Save(); }
    }

    /// <summary>When false, the local .mp4 is deleted after a confirmed successful upload.</summary>
    public static bool KeepLocalCopy
    {
        get => _current.KeepLocalCopy;
        set { _current.KeepLocalCopy = value; Save(); }
    }

    /// <summary>The active output directory (chosen folder, or the default).</summary>
    public static string OutputDirectory => OutputFolder;

    public static string BuildOutputPath(DateTime timestamp) =>
        Path.Combine(OutputDirectory, $"wipshare_{timestamp:yyyyMMdd_HHmmss}.mp4");

    /// <summary>Thumbnail JPEG path sibling to an MP4 path (…\foo.mp4 → …\foo.jpg).</summary>
    public static string ThumbnailPathFor(string mp4Path) => Path.ChangeExtension(mp4Path, ".jpg");

    /// <summary>Upload sidecar path sibling to an MP4 path (…\foo.mp4 → …\foo.upload.json).</summary>
    public static string SidecarPathFor(string mp4Path) => Path.ChangeExtension(mp4Path, ".upload.json");

    private static void Save()
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var tmp = SettingsFilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_current, JsonOpts));
                File.Move(tmp, SettingsFilePath, overwrite: true);
            }
            catch
            {
                // Best-effort: the in-memory value still applies for this session.
            }
        }
    }

    /// <summary>The on-disk shape. Defaults here are the first-run values.</summary>
    internal sealed class Persisted
    {
        public int HotkeyModifiers { get; set; } = (int)(ModifierKeys.Control | ModifierKeys.Shift);
        public int HotkeyKey { get; set; } = (int)Key.R;
        public bool CountdownEnabled { get; set; } = true;
        public string? OutputFolder { get; set; }   // null → DefaultOutputDirectory
        public bool AutoCopyLink { get; set; } = true;
        public bool KeepLocalCopy { get; set; } = true;

        public static Persisted Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var p = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(SettingsFilePath), JsonOpts);
                    if (p != null) return p;
                }
            }
            catch
            {
                // Corrupt/unreadable → start from defaults.
            }
            return new Persisted();
        }
    }
}
