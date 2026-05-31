namespace WipShare.Client.Config;

/// <summary>
/// Compile-time constants that are never user-facing or user-editable.
/// </summary>
public static class Constants
{
    /// <summary>
    /// The WipShare backend. Hardcoded on purpose — the server URL is not a
    /// setting, not in config.json, and never shown in any UI. Only the invite
    /// code (stored in Windows Credential Manager) is user-supplied.
    /// </summary>
    public const string UploadBaseUrl = "https://wipshare-web.vercel.app";
}
