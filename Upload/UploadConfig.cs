using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WipShare.Client.Settings;

namespace WipShare.Client.Upload;

/// <summary>
/// Upload configuration loaded from %LOCALAPPDATA%\WipShare\config.json. The
/// secret lives here (hand-filled by the user once) and never in source. On
/// first run we write a placeholder file and uploads stay disabled until the
/// user pastes a real secret.
/// </summary>
public sealed class UploadConfig
{
    public const string PlaceholderSecret = "PASTE_SECRET_HERE";
    public const string DefaultBaseUrl = "https://wipshare-web.vercel.app";

    [JsonPropertyName("uploadBaseUrl")]
    public string UploadBaseUrl { get; set; } = DefaultBaseUrl;

    [JsonPropertyName("uploadSecret")]
    public string UploadSecret { get; set; } = PlaceholderSecret;

    /// <summary>True only when both a base URL and a non-placeholder secret are present.</summary>
    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(UploadBaseUrl)
        && !string.IsNullOrWhiteSpace(UploadSecret)
        && UploadSecret != PlaceholderSecret;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Loads config.json, or creates it with placeholders if absent. Never throws:
    /// on any error returns an unconfigured default so the app still records.
    /// </summary>
    public static UploadConfig LoadOrCreate(string path, ILogger logger)
    {
        try
        {
            if (!File.Exists(path))
            {
                var fresh = new UploadConfig();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(fresh, JsonOptions));
                logger.LogWarning(
                    "Created {Path} with a placeholder secret. Upload is DISABLED until you set uploadSecret.",
                    path);
                return fresh;
            }

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<UploadConfig>(json, JsonOptions);
            if (cfg is null)
            {
                logger.LogError("config.json at {Path} deserialized to null; upload disabled", path);
                return new UploadConfig { UploadSecret = PlaceholderSecret };
            }

            if (!cfg.IsConfigured)
                logger.LogWarning("Upload not configured (placeholder/empty secret in {Path}); upload disabled", path);
            else
                logger.LogInformation("Upload configured: base={Base} (secret hidden)", cfg.UploadBaseUrl);

            return cfg;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load config.json at {Path}; upload disabled", path);
            return new UploadConfig { UploadSecret = PlaceholderSecret };
        }
    }
}
