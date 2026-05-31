using System.IO;

namespace WipShare.Client.Settings;

public static class AppSettings
{
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

    // Phase 2B — cloud upload. UploadBaseUrl / UploadSecret live in config.json
    // (LocalAppData), NOT here, so the secret never ships in source.
    public const bool AutoUpload = true;
    public const bool KeepLocalCopy = true;

    /// <summary>Index of the captured frame retained as the poster thumbnail (~1s at 30 fps).</summary>
    public const int PosterFrameIndex = 30;
    /// <summary>Longest-side cap (px) for the generated JPEG thumbnail.</summary>
    public const int ThumbnailMaxEdge = 1280;

    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WipShare");

    public static string LogFilePath => Path.Combine(LogDirectory, "log.txt");

    public static string ConfigFilePath => Path.Combine(LogDirectory, "config.json");

    /// <summary>Per-device identity (owner_token). Not a secret — plain JSON.</summary>
    public static string IdentityFilePath => Path.Combine(LogDirectory, "identity.json");

    public static string OutputDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", "WipShare");

    public static string BuildOutputPath(DateTime timestamp) =>
        Path.Combine(OutputDirectory, $"wipshare_{timestamp:yyyyMMdd_HHmmss}.mp4");

    /// <summary>Thumbnail JPEG path sibling to an MP4 path (…\foo.mp4 → …\foo.jpg).</summary>
    public static string ThumbnailPathFor(string mp4Path) =>
        Path.ChangeExtension(mp4Path, ".jpg");

    /// <summary>Upload sidecar path sibling to an MP4 path (…\foo.mp4 → …\foo.upload.json).</summary>
    public static string SidecarPathFor(string mp4Path) =>
        Path.ChangeExtension(mp4Path, ".upload.json");
}
