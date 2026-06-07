using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WipShare.Client.Settings;

namespace WipShare.Client.Upload;

/// <summary>
/// Lifecycle of a single upload. Persisted to disk so an interrupted upload
/// (crash, reboot, network drop) can be resumed on the next launch.
/// </summary>
public enum UploadState
{
    Pending,
    InitiatingThenUploading,
    Completing,
    Done,
    Failed,
}

/// <summary>
/// One queued upload plus its durable state. Backed by a <c>.upload.json</c>
/// sidecar sitting next to the MP4 (…\foo.mp4 → …\foo.upload.json). Every state
/// change is written atomically (temp file + move) so a torn write can never
/// corrupt the sidecar; the sidecar is deleted once the upload completes.
/// </summary>
public sealed class UploadJob
{
    [JsonPropertyName("clipLocalMp4Path")] public string ClipLocalMp4Path { get; set; } = "";
    [JsonPropertyName("clipLocalJpgPath")] public string? ClipLocalJpgPath { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("mp4SizeBytes")] public long Mp4SizeBytes { get; set; }
    [JsonPropertyName("jpgSizeBytes")] public long? JpgSizeBytes { get; set; }

    [JsonPropertyName("state")] public UploadState State { get; set; } = UploadState.Pending;
    [JsonPropertyName("attemptCount")] public int AttemptCount { get; set; }
    [JsonPropertyName("clipId")] public string? ClipId { get; set; }
    [JsonPropertyName("viewerUrl")] public string? ViewerUrl { get; set; }
    [JsonPropertyName("lastError")] public string? LastError { get; set; }

    /// <summary>The sidecar path derived from the MP4 path.</summary>
    [JsonIgnore]
    public string SidecarPath => AppSettings.SidecarPathFor(ClipLocalMp4Path);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Atomically writes the sidecar (temp file then move-overwrite). Best-effort:
    /// logs and swallows on failure - a sidecar write must never crash an upload.
    /// </summary>
    public void Save(ILogger logger)
    {
        try
        {
            var path = SidecarPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist upload sidecar for {Path}", ClipLocalMp4Path);
        }
    }

    /// <summary>Removes the sidecar (and any stray temp file). Best-effort.</summary>
    public void DeleteSidecar(ILogger logger)
    {
        try
        {
            var path = SidecarPath;
            if (File.Exists(path)) File.Delete(path);
            var tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete upload sidecar for {Path}", ClipLocalMp4Path);
        }
    }

    /// <summary>Loads a sidecar, or null if it is missing/unreadable/empty.</summary>
    public static UploadJob? TryLoad(string sidecarPath, ILogger logger)
    {
        try
        {
            var json = File.ReadAllText(sidecarPath);
            var job = JsonSerializer.Deserialize<UploadJob>(json, JsonOptions);
            if (job is null || string.IsNullOrWhiteSpace(job.ClipLocalMp4Path))
            {
                logger.LogWarning("Ignoring unreadable/empty upload sidecar {Path}", sidecarPath);
                return null;
            }
            return job;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load upload sidecar {Path}", sidecarPath);
            return null;
        }
    }
}
