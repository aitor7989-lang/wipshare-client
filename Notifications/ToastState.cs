namespace WipShare.Client.Notifications;

/// <summary>The four toast states from the notifications design.</summary>
public enum ToastKind
{
    /// <summary>Upload in flight: icon, filename, MB/seconds-left line, progress bar. Persists.</summary>
    Uploading,
    /// <summary>Link copied: poster thumb, "Link copied", viewer URL, Open + Copy again. Auto-dismiss 7s.</summary>
    Success,
    /// <summary>Upload failed: alert icon, "Saved locally…", Retry now. Persists until dismissed.</summary>
    Failed,
    /// <summary>Saved but sharing not configured: neutral save icon, "Set up sharing". Auto-dismiss 7s.</summary>
    LocalOnly,
}

/// <summary>
/// Plain data describing a toast's current content. Mutated as an upload
/// progresses; the <see cref="ToastWindow"/> renders whatever this holds.
/// Carries no behavior and no WPF types so it stays trivially testable.
/// </summary>
public sealed class ToastViewModel
{
    public ToastKind Kind { get; set; } = ToastKind.Uploading;

    /// <summary>Display filename (no directory), e.g. "wipshare_20260530_2231.mp4".</summary>
    public string FileName { get; set; } = "";

    /// <summary>Local poster JPEG path for the success thumbnail; null → placeholder.</summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>Canonical viewer URL once the upload completes (success state).</summary>
    public string? ViewerUrl { get; set; }

    // Uploading-state values (real, from the job).
    public double SentMb { get; set; }
    public double TotalMb { get; set; }

    /// <summary>0..1 upload fraction for the progress bar.</summary>
    public double ProgressFraction { get; set; }

    /// <summary>Trailing dim note: "starting…", "{n}s left", "done", "retrying…".</summary>
    public string ProgressNote { get; set; } = "starting…";
}
