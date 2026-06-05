using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using WipShare.Client.Capture;
using WipShare.Client.Config;
using WipShare.Client.Encoding;
using WipShare.Client.Hotkey;
using WipShare.Client.Logging;
using WipShare.Client.Native;
using WipShare.Client.Notifications;
using WipShare.Client.Recording;
using WipShare.Client.Settings;
using WipShare.Client.Setup;
using WipShare.Client.Tray;
using WipShare.Client.Upload;

namespace WipShare.Client;

public partial class App : Application
{
    private TrayApp? _trayApp;
    private HotkeyManager? _hotkeyManager;
    private MfRuntime? _mfRuntime;
    private EncoderInfo? _encoder;
    private FileLoggerProvider? _fileLoggerProvider;
    private ILoggerFactory? _loggerFactory;
    private ILogger<App>? _logger;
    private int _captureInFlight; // 0 = idle, 1 = capturing

    // Cloud upload. Null until an invite code is connected (recordings then
    // stay local + Explorer pop). The code lives in Credential Manager via
    // AccessConfig; the backend URL is the hardcoded Constants.UploadBaseUrl.
    private AccessConfig? _accessConfig;
    private WipShareApiClient? _apiClient;
    private UploadQueue? _uploadQueue;
    private FirstRunWindow? _firstRunWindow;

    // Desktop toast stack (replaces the upload tray balloons). Maps each in-flight
    // job to its single toast window so progress/success/failure transition in place.
    private ToastHost? _toastHost;
    private readonly Dictionary<UploadJob, ToastWindow> _toasts = new();
    private readonly Dictionary<UploadJob, long> _uploadStartTick = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _fileLoggerProvider = new FileLoggerProvider(AppSettings.LogFilePath);
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new DebugLoggerProvider());
            builder.AddProvider(_fileLoggerProvider);
            builder.SetMinimumLevel(LogLevel.Information);
        });

        _logger = _loggerFactory.CreateLogger<App>();
        var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";
        _logger.LogInformation("WipShare starting (v{Version})", version);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _trayApp = new TrayApp(_loggerFactory);
        _trayApp.Initialize();
        _trayApp.RetryFailedRequested += OnRetryFailedUploads;
        _trayApp.ChangeInviteCodeRequested += (_, _) => ShowFirstRunWindow();

        // Toast stack — created regardless of upload config (the local-only toast
        // fires from the capture path when there's no invite code).
        _toastHost = new ToastHost(_loggerFactory);

        try
        {
            _mfRuntime = new MfRuntime();
            _encoder = new HardwareEncoderProbe(_loggerFactory).Select();
            _logger.LogInformation(
                "Selected H.264 encoder: {Kind} '{Name}' (CLSID={Clsid}, Hardware={Hw})",
                _encoder.Kind, _encoder.FriendlyName, _encoder.Clsid, _encoder.IsHardware);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Encoder probe failed; recordings will fail until this is resolved");
            _trayApp.ShowBalloon("WipShare", $"Encoder probe failed: {ex.Message}",
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
        }

        _hotkeyManager = new HotkeyManager(_loggerFactory);
        _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
        try
        {
            _hotkeyManager.Register();
        }
        catch (Exception ex)
        {
            _trayApp.ShowBalloon("WipShare", $"Hotkey registration failed: {ex.Message}",
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
        }

        InitializeUpload();
    }

    /// <summary>
    /// On startup: load access identity, migrate any legacy plaintext secret into
    /// Credential Manager, then either start the upload pipeline (code present) or
    /// show the first-run window (no code). Non-blocking — the tray icon is always
    /// available regardless.
    /// </summary>
    private void InitializeUpload()
    {
        _accessConfig = new AccessConfig(_logger!);
        _accessConfig.MigrateLegacyConfigIfPresent();

        if (_accessConfig.HasCode)
        {
            StartUploadPipeline();
        }
        else
        {
            _logger?.LogInformation("No invite code yet — showing first-run window (recordings stay local until connected)");
            ShowFirstRunWindow();
        }
    }

    /// <summary>Shows (or re-focuses) the first-run / change-code window without blocking the tray.</summary>
    private void ShowFirstRunWindow()
    {
        if (_accessConfig is null || _loggerFactory is null) return;

        if (_firstRunWindow != null)
        {
            _firstRunWindow.Activate();
            return;
        }

        var win = new FirstRunWindow(_accessConfig, _loggerFactory);
        _firstRunWindow = win;
        win.Closed += (_, _) => { if (ReferenceEquals(_firstRunWindow, win)) _firstRunWindow = null; };
        win.CodeAccepted += (_, _) => RestartUploadPipeline();
        win.Show();
        win.Activate();
    }

    /// <summary>(Re)builds the upload pipeline from the current invite code + owner token.</summary>
    private void StartUploadPipeline()
    {
        // Local (not the const directly) so the guard isn't folded to dead code.
        bool autoUpload = AppSettings.AutoUpload;
        if (!autoUpload)
        {
            _logger?.LogInformation("Auto-upload disabled in settings — recordings stay local");
            return;
        }

        var code = _accessConfig?.GetCode();
        if (string.IsNullOrWhiteSpace(code))
        {
            _logger?.LogInformation("No invite code available — upload stays off");
            return;
        }

        _apiClient = new WipShareApiClient(Constants.UploadBaseUrl, code, _accessConfig!.GetOwnerToken(), _loggerFactory!);
        _uploadQueue = new UploadQueue(_apiClient, new UploadCallbacks
        {
            ActiveCountChanged = OnUploadActiveCountChanged,
            Started = OnUploadStarted,
            Progress = OnUploadProgress,
            Succeeded = OnUploadSucceeded,
            Failed = OnUploadFailed,
        }, _loggerFactory!);

        try
        {
            int resumed = _uploadQueue.RescanAndEnqueueIncomplete(AppSettings.OutputDirectory);
            if (resumed > 0)
                _trayApp?.ShowBalloon("WipShare", $"Resuming {resumed} interrupted upload(s)…");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Orphaned-upload scan failed");
        }

        _logger?.LogInformation("Upload pipeline ready (base={Base})", Constants.UploadBaseUrl);
    }

    /// <summary>Tears down the existing pipeline (if any) and rebuilds with the current code.</summary>
    private void RestartUploadPipeline()
    {
        TearDownUploadPipeline();
        StartUploadPipeline();
        _trayApp?.ShowBalloon("WipShare", "Connected — uploads are on.");
    }

    private void TearDownUploadPipeline()
    {
        if (_uploadQueue != null)
        {
            try { _uploadQueue.ShutdownAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
            catch (Exception ex) { _logger?.LogError(ex, "Upload queue shutdown (restart) failed"); }
            _uploadQueue.Dispose();
            _uploadQueue = null;
        }
        _apiClient?.Dispose();
        _apiClient = null;
    }

    private async void OnHotkeyPressed(object? sender, EventArgs e)
    {
        if (Interlocked.CompareExchange(ref _captureInFlight, 1, 0) != 0)
        {
            _logger?.LogInformation("Hotkey ignored: capture already in flight");
            return;
        }

        try
        {
            // For diagnostic context only — region capture doesn't gate on the
            // focused window like Phase 1's window capture did.
            var hwnd = Win32Interop.GetForegroundWindow();
            _logger?.LogInformation("Hotkey pressed; foreground HWND=0x{Hwnd:X} title='{Title}'",
                hwnd.ToInt64(), Win32Interop.GetWindowTitle(hwnd));

            await RecordRegionAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Hotkey handler threw");
        }
        finally
        {
            Interlocked.Exchange(ref _captureInFlight, 0);
        }
    }

    private async Task RecordRegionAsync()
    {
        using var selector = new RegionSelector(_loggerFactory!);
        var result = await selector.SelectAsync();
        switch (result.Outcome)
        {
            case SelectionOutcome.Canceled:
                _logger?.LogInformation("Selection canceled — no recording");
                return;
            case SelectionOutcome.TooSmall:
                _trayApp?.ShowBalloon("WipShare", "Region too small. Try again.",
                    BalloonIcon.Warning);
                return;
            case SelectionOutcome.Confirmed:
                await RecordSelectedRegionAsync(result.Region!.Value, result.SelectionDipRect, selector);
                return;
        }
    }

    private async Task RecordSelectedRegionAsync(SelectedRegion region, Rect dipRect, RegionSelector selector)
    {
        // Take over the selector's window for the quiet 1px region border. It
        // stays up through the countdown and the recording (capture-excluded).
        var overlayWindow = selector.OverlayWindow
            ?? throw new InvalidOperationException("OverlayWindow disappeared between SelectAsync and RecordSelectedRegionAsync");
        using var recordingOverlay = new RecordingOverlay(overlayWindow, dipRect, _loggerFactory!);
        recordingOverlay.Show();

        var total = TimeSpan.FromSeconds(AppSettings.RecordDurationSeconds);

        // One Esc hook spans the whole capture (countdown + recording). The
        // overlays never steal focus, so the recorded app stays active.
        using var escHook = new GlobalEscHook();
        using var cts = new CancellationTokenSource();        // discard (Cancel/Esc) — whole flow
        using var finishCts = new CancellationTokenSource();  // stop early & keep (Finish)
        bool canceled = false;
        bool finished = false;
        bool restartRequested = false;
        CaptureSession? activeSession = null;            // captured by pause/restart handlers
        CancellationTokenSource? activeRestartCts = null;

        var pill = new RecordingPill(region, total, _loggerFactory!);

        void RequestCancel()
        {
            if (canceled) return;
            canceled = true;
            _logger?.LogInformation("Capture cancel requested (Esc/cancel)");
            try { cts.Cancel(); } catch { /* ignore */ }
        }
        void RequestFinish()
        {
            if (canceled || finished) return;
            finished = true;
            _logger?.LogInformation("Finish requested — stopping early, keeping clip");
            try { finishCts.Cancel(); } catch { /* ignore */ }
        }
        void RequestRestart()
        {
            if (canceled) return;
            restartRequested = true;
            _logger?.LogInformation("Restart requested — discarding take, re-counting down");
            try { activeRestartCts?.Cancel(); } catch { /* ignore */ }
        }
        escHook.Pressed += RequestCancel;
        pill.CancelRequested += RequestCancel;
        pill.FinishRequested += RequestFinish;
        pill.RestartRequested += RequestRestart;
        pill.PauseToggleRequested += paused =>
        {
            var s = activeSession;
            if (s is null) return;
            if (paused) s.Pause(); else s.Resume();
        };

        try
        {
            pill.Show();
            bool firstTake = true;

            while (true)
            {
                if (!firstTake) pill.ResetToCountdown();
                firstTake = false;
                restartRequested = false;

                // ----- 3-2-1 countdown, in the pill (skipped when disabled in Settings) -----
                if (AppSettings.CountdownEnabled)
                {
                    bool counted = await pill.RunCountdownAsync(cts.Token);
                    if (!counted || canceled)
                    {
                        _logger?.LogInformation("Canceled during countdown — no file");
                        return;
                    }
                }

                // ----- recording -----
                pill.SwitchToRecording();
                var outputPath = AppSettings.BuildOutputPath(DateTime.Now);
                var session = new CaptureSession(
                    region, outputPath, total, AppSettings.TargetFps, AppSettings.TargetBitrateBps, _loggerFactory!);
                using var restartCts = new CancellationTokenSource();
                activeSession = session;
                activeRestartCts = restartCts;
                var progress = new Progress<TimeSpan>(pill.SetElapsed);

                RecordingResult? result = null;
                try
                {
                    result = await Task.Run(() => session.RunAsync(cts.Token, progress, finishCts.Token, restartCts.Token));
                    _logger?.LogInformation(
                        "Recording done: {Encoded} frames ({Dropped} dropped, finished={Finished}, restart={Restart}) → {Path}",
                        result.FramesEncoded, result.FramesDropped, finished, restartRequested, result.OutputPath);
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Recording failed");
                    _trayApp?.ShowBalloon("WipShare", $"Recording failed: {ex.Message}", BalloonIcon.Error);
                }
                finally
                {
                    session.Dispose();
                    activeSession = null;
                    activeRestartCts = null;
                }

                if (restartRequested && !canceled && !finished)
                {
                    DiscardRecording(outputPath);  // clean teardown done; drop the partial take
                    continue;                      // re-enter the countdown for the same region
                }
                if (canceled)
                {
                    _logger?.LogInformation("Recording aborted — discarding {Path}", outputPath);
                    DiscardRecording(outputPath);
                    return;
                }
                if (result is null) return; // hard failure (already surfaced)

                if (_uploadQueue != null)
                    EnqueueUpload(result);              // uploading toast appears via the queue's Started callback
                else
                    ShowLocalOnlyToast(result.OutputPath, result.ThumbnailPath);
                return;
            }
        }
        finally
        {
            try { pill.Close(); } catch { /* already closing */ }
        }
    }

    /// <summary>Deletes a canceled recording's MP4 and its sibling poster JPEG.</summary>
    private void DiscardRecording(string mp4Path)
    {
        try { if (File.Exists(mp4Path)) File.Delete(mp4Path); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Could not delete canceled recording {Path}", mp4Path); }

        var jpg = AppSettings.ThumbnailPathFor(mp4Path);
        try { if (File.Exists(jpg)) File.Delete(jpg); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Could not delete canceled thumbnail {Path}", jpg); }
    }

    private void RevealInExplorer(string path)
    {
        if (!File.Exists(path))
        {
            _logger?.LogWarning("Cannot reveal in Explorer — file missing: {Path}", path);
            return;
        }

        try
        {
            // explorer.exe /select,"path" — quoting the path with embedded comma per shell convention.
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to launch Explorer for {Path}", path);
        }
    }

    private void EnqueueUpload(RecordingResult result)
    {
        try
        {
            var mp4 = new FileInfo(result.OutputPath);
            if (!mp4.Exists || mp4.Length == 0)
            {
                _logger?.LogWarning("Skipping upload — MP4 missing or empty: {Path}", result.OutputPath);
                RevealInExplorer(result.OutputPath);
                return;
            }

            string? jpgPath = result.ThumbnailPath;
            long? jpgSize = null;
            if (jpgPath != null)
            {
                var jpg = new FileInfo(jpgPath);
                if (jpg.Exists && jpg.Length > 0) jpgSize = jpg.Length;
                else jpgPath = null; // generated thumbnail missing — upload without it
            }

            var job = new UploadJob
            {
                ClipLocalMp4Path = result.OutputPath,
                ClipLocalJpgPath = jpgPath,
                Width = result.Width,
                Height = result.Height,
                Mp4SizeBytes = mp4.Length,
                JpgSizeBytes = jpgSize,
                State = UploadState.Pending,
            };

            _uploadQueue!.Enqueue(job);
            _logger?.LogInformation(
                "Enqueued upload: {Path} ({W}x{H}, {Bytes} bytes, thumb={Thumb})",
                result.OutputPath, result.Width, result.Height, mp4.Length, jpgPath != null);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to enqueue upload for {Path}; revealing locally instead", result.OutputPath);
            RevealInExplorer(result.OutputPath);
        }
    }

    private void OnUploadActiveCountChanged(int activeCount) => _trayApp?.SetUploadStatus(activeCount);

    // The upload callbacks are raised on the queue's background thread; every
    // handler marshals to the UI thread before touching toasts / clipboard / tray.

    private void OnUploadStarted(UploadJob job)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_toastHost is null) return;
            _uploadStartTick[job] = Environment.TickCount64;

            if (_toasts.TryGetValue(job, out var existing))
            {
                existing.TransitionToUploading("uploading…"); // a retry reuses the same toast
                return;
            }

            var vm = new ToastViewModel
            {
                Kind = ToastKind.Uploading,
                FileName = Path.GetFileName(job.ClipLocalMp4Path),
                TotalMb = job.Mp4SizeBytes / (1024.0 * 1024.0),
                ProgressNote = "starting…",
            };
            var w = _toastHost.AddToast(vm);
            w.OpenRequested += _ => OpenViewer(job);
            w.CopyAgainRequested += _ => { if (!string.IsNullOrWhiteSpace(job.ViewerUrl)) CopyToClipboardWithRetry(job.ViewerUrl!); };
            w.RetryRequested += _ => RetryUpload(job, w);
            w.Dismissed += _ => { _toasts.Remove(job); _uploadStartTick.Remove(job); };
            _toasts[job] = w;
        });
    }

    private void OnUploadProgress(UploadJob job, long sent, long total)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_toasts.TryGetValue(job, out var w)) return;
            const double mb = 1024.0 * 1024.0;
            double frac = total > 0 ? Math.Clamp((double)sent / total, 0, 1) : 0;

            string note;
            if (sent <= 0) note = "starting…";
            else if (frac >= 0.999) note = "done";
            else
            {
                long startTick = _uploadStartTick.TryGetValue(job, out var t0) ? t0 : Environment.TickCount64;
                double elapsedSec = Math.Max(0.25, (Environment.TickCount64 - startTick) / 1000.0);
                double rate = sent / elapsedSec; // bytes/sec
                int secs = rate > 1 ? Math.Max(1, (int)Math.Ceiling((total - sent) / rate)) : 1;
                note = $"{secs}s left";
            }
            w.UpdateProgress(sent / mb, total / mb, frac, note);
        });
    }

    private void OnUploadSucceeded(UploadJob job)
    {
        var url = job.ViewerUrl;
        Dispatcher.BeginInvoke(() =>
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                // Auto-copy can be turned off in Settings — the toast still offers Copy.
                if (AppSettings.AutoCopyLink) CopyToClipboardWithRetry(url!);
                _trayApp?.SetLastLink(url!);
            }
            // Transition (which caches the poster into memory) BEFORE any local cleanup.
            if (_toasts.TryGetValue(job, out var w) && !string.IsNullOrWhiteSpace(url))
                w.TransitionToSuccess(url!, job.ClipLocalJpgPath);

            // "Keep a local copy = off" → remove the mp4 (+poster) now that the upload
            // is confirmed. This path only runs on a successful upload — never local-only.
            if (!AppSettings.KeepLocalCopy)
                DeleteLocalCopyAfterUpload(job);
        });
    }

    /// <summary>Deletes the local mp4 + poster after a confirmed upload (keep-local off). Best-effort.</summary>
    private void DeleteLocalCopyAfterUpload(UploadJob job)
    {
        try { if (File.Exists(job.ClipLocalMp4Path)) File.Delete(job.ClipLocalMp4Path); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Could not remove local mp4 after upload"); }

        var jpg = job.ClipLocalJpgPath;
        try { if (!string.IsNullOrEmpty(jpg) && File.Exists(jpg)) File.Delete(jpg); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Could not remove local poster after upload"); }

        _logger?.LogInformation("Local copy removed after successful upload (keep-local off)");
    }

    private void OnUploadFailed(UploadJob job)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_toasts.TryGetValue(job, out var w)) w.TransitionToFailed();
            else _trayApp?.ShowBalloon("WipShare", "Upload failed — will retry on restart.", BalloonIcon.Warning);
        });
    }

    /// <summary>Re-enqueues a failed job and morphs its toast back to uploading (Retry now).</summary>
    private void RetryUpload(UploadJob job, ToastWindow w)
    {
        if (_uploadQueue is null) return;
        w.TransitionToUploading("retrying…");
        _uploadStartTick[job] = Environment.TickCount64;
        job.State = UploadState.Pending;
        job.AttemptCount = 0;
        _uploadQueue.Enqueue(job);
    }

    private void OpenViewer(UploadJob job)
    {
        var url = job.ViewerUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch (Exception ex) { _logger?.LogError(ex, "Open viewer failed"); }
    }

    /// <summary>Shows the calm "Saved to your computer" toast when sharing isn't set up.</summary>
    private void ShowLocalOnlyToast(string mp4Path, string? thumbPath)
    {
        if (_toastHost is null) { RevealInExplorer(mp4Path); return; }
        var vm = new ToastViewModel
        {
            Kind = ToastKind.LocalOnly,
            FileName = Path.GetFileName(mp4Path),
            ThumbnailPath = thumbPath,
        };
        var w = _toastHost.AddToast(vm);
        w.SetupRequested += _ => ShowFirstRunWindow();
        w.ShowAsLocalOnly();
    }

    private void OnRetryFailedUploads(object? sender, EventArgs e)
    {
        if (_uploadQueue is null)
        {
            _trayApp?.ShowBalloon("WipShare", "Upload is not configured.", BalloonIcon.Warning);
            return;
        }

        try
        {
            int n = _uploadQueue.RescanAndEnqueueIncomplete(AppSettings.OutputDirectory);
            _trayApp?.ShowBalloon("WipShare", n > 0 ? $"Retrying {n} upload(s)…" : "No uploads to retry.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Retry-failed scan failed");
        }
    }

    private void CopyToClipboardWithRetry(string text)
    {
        for (int i = 0; i < 3; i++)
        {
            try { Clipboard.SetText(text); return; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Clipboard set failed (attempt {N})", i + 1);
                Thread.Sleep(80);
            }
        }
        _logger?.LogError("Could not copy link to clipboard after retries");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("WipShare exiting (code={Code})", e.ApplicationExitCode);
        _hotkeyManager?.Dispose();

        // Give in-flight uploads a brief grace period; anything unfinished
        // persists its sidecar and resumes on the next launch.
        if (_uploadQueue != null)
        {
            try { _uploadQueue.ShutdownAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
            catch (Exception ex) { _logger?.LogError(ex, "Upload queue shutdown failed"); }
            _uploadQueue.Dispose();
        }
        _apiClient?.Dispose();

        try { _toastHost?.Dispose(); } catch (Exception ex) { _logger?.LogError(ex, "Toast host dispose"); }
        _trayApp?.Dispose();
        _mfRuntime?.Dispose();
        _loggerFactory?.Dispose();
        _fileLoggerProvider?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled dispatcher exception");
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.ExceptionObject as Exception, "Unhandled AppDomain exception (terminating={Terminating})", e.IsTerminating);
    }

    private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}
