using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WipShare.Client.Settings;
using WipShare.Client.Upload;

namespace WipShare.Client.Config;

/// <summary>
/// Owns the two pieces of connection identity:
///  • the invite code (== the shared upload secret) - stored in Windows
///    Credential Manager, never in plaintext;
///  • a per-device owner_token (a GUID) - stored in identity.json (not a secret).
///
/// On first construction it migrates any legacy plaintext secret from config.json
/// into Credential Manager, then leaves config.json alone.
/// </summary>
public sealed class AccessConfig
{
    private readonly ILogger _logger;
    private string? _ownerToken;

    public AccessConfig(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>True when a (non-empty) invite code is stored in Credential Manager.</summary>
    public bool HasCode => !string.IsNullOrWhiteSpace(GetCode());

    /// <summary>The stored invite code / upload secret, or null if none is set.</summary>
    public string? GetCode()
    {
        try
        {
            return WindowsCredential.Read();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reading invite code from Credential Manager failed");
            return null;
        }
    }

    /// <summary>Persists the invite code to Credential Manager. Throws on failure.</summary>
    public void SaveCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("code is empty", nameof(code));
        WindowsCredential.Write(code.Trim());
        _logger.LogInformation("Invite code saved to Windows Credential Manager");
    }

    /// <summary>Removes the stored invite code (best-effort).</summary>
    public void ClearCode()
    {
        try { WindowsCredential.Delete(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Clearing invite code failed"); }
    }

    /// <summary>
    /// The per-device owner_token, generated and persisted on first use. Stable
    /// across runs. Not a secret - it only associates uploads with this device.
    /// </summary>
    public string GetOwnerToken()
    {
        return _ownerToken ??= LoadOrCreateOwnerToken();
    }

    private string LoadOrCreateOwnerToken()
    {
        var path = AppSettings.IdentityFilePath;
        try
        {
            if (File.Exists(path))
            {
                var doc = JsonSerializer.Deserialize<Identity>(File.ReadAllText(path), JsonOptions);
                if (!string.IsNullOrWhiteSpace(doc?.OwnerToken)) return doc!.OwnerToken!;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "identity.json unreadable; regenerating owner token");
        }

        var token = Guid.NewGuid().ToString();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new Identity { OwnerToken = token }, JsonOptions));
            _logger.LogInformation("Generated a new device owner token");
        }
        catch (Exception ex)
        {
            // An ephemeral token still lets uploads succeed; it just won't persist.
            _logger.LogError(ex, "Writing identity.json failed; using an ephemeral owner token this run");
        }
        return token;
    }

    /// <summary>
    /// One-time migration: if a pre-2C config.json holds a real (non-placeholder)
    /// uploadSecret, move it into Credential Manager. Subsequent launches see
    /// HasCode == true and skip this. config.json is then ignored.
    /// </summary>
    public void MigrateLegacyConfigIfPresent()
    {
        if (HasCode) return;
        try
        {
            var path = AppSettings.ConfigFilePath;
            if (!File.Exists(path)) return;

            var doc = JsonSerializer.Deserialize<LegacyConfig>(File.ReadAllText(path), JsonOptions);
            var secret = doc?.UploadSecret;
            if (string.IsNullOrWhiteSpace(secret) || secret == UploadConfig.PlaceholderSecret) return;

            SaveCode(secret);
            _logger.LogInformation("Migrated legacy upload secret from config.json into Credential Manager");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Legacy config.json migration skipped");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private sealed class Identity
    {
        [JsonPropertyName("ownerToken")] public string? OwnerToken { get; set; }
    }

    private sealed class LegacyConfig
    {
        [JsonPropertyName("uploadSecret")] public string? UploadSecret { get; set; }
    }
}

/// <summary>
/// Thin P/Invoke wrapper over the Windows Credential Manager (advapi32) for a
/// single generic credential. The secret blob is stored as UTF-16 bytes.
/// </summary>
internal static class WindowsCredential
{
    // One stable target name for WipShare's invite code / upload secret.
    private const string TargetName = "WipShare:InviteCode";
    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint reservedFlag);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);

    public static string? Read()
    {
        if (!CredRead(TargetName, CRED_TYPE_GENERIC, 0, out IntPtr ptr)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0) return null;
            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            // Fully qualified: a sibling WipShare.Client.Encoding namespace otherwise
            // shadows System.Text.Encoding here.
            return System.Text.Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public static void Write(string secret)
    {
        var blob = System.Text.Encoding.Unicode.GetBytes(secret);
        IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = TargetName,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = "WipShare",
            };
            if (!CredWrite(ref cred, 0))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "CredWrite failed");
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    public static void Delete()
    {
        // Ignore "not found"; any other failure is non-fatal for our use.
        CredDelete(TargetName, CRED_TYPE_GENERIC, 0);
    }
}
