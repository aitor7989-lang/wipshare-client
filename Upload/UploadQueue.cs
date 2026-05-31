using System.IO;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace WipShare.Client.Upload;

/// <summary>
/// UI-thread-agnostic notifications raised by the queue. The host (App) wires
/// these to the tray and clipboard, marshalling to the UI thread as needed.
/// Keeps this namespace free of any WPF/tray dependency.
/// </summary>
public sealed class UploadCallbacks
{
    /// <summary>Active job count changed (queued + in-flight). For the tray badge.</summary>
    public Action<int>? ActiveCountChanged { get; init; }
    /// <summary>A job has been picked up and its pipeline is starting. For the uploading toast.</summary>
    public Action<UploadJob>? Started { get; init; }
    /// <summary>Upload progress: (job, cumulative bytes sent, total mp4 bytes).</summary>
    public Action<UploadJob, long, long>? Progress { get; init; }
    /// <summary>An upload finished; <see cref="UploadJob.ViewerUrl"/> is set.</summary>
    public Action<UploadJob>? Succeeded { get; init; }
    /// <summary>An upload failed permanently (this run); the sidecar persists for restart.</summary>
    public Action<UploadJob>? Failed { get; init; }
}

/// <summary>
/// Single-consumer background upload pump. Jobs flow through an unbounded
/// <see cref="Channel{T}"/>; one worker runs each job's pipeline
/// (initiate → PUT mp4 → PUT thumb (best-effort) → complete) with retry/backoff
/// on transient failures. State is persisted to a sidecar on every transition so
/// interrupted uploads resume on the next launch.
/// </summary>
public sealed class UploadQueue : IDisposable
{
    // Backoff schedule for transient failures: 3 retries after the first attempt.
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(5),
    };

    private readonly WipShareApiClient _api;
    private readonly UploadCallbacks _callbacks;
    private readonly ILogger<UploadQueue> _logger;
    private readonly Channel<UploadJob> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumer;

    private int _activeCount;
    private bool _disposed;

    public UploadQueue(WipShareApiClient api, UploadCallbacks callbacks, ILoggerFactory loggerFactory)
    {
        _api = api;
        _callbacks = callbacks;
        _logger = loggerFactory.CreateLogger<UploadQueue>();
        _channel = Channel.CreateUnbounded<UploadJob>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false,
        });
        _consumer = Task.Run(() => ConsumeAsync(_cts.Token));
    }

    /// <summary>Queued + in-flight jobs.</summary>
    public int ActiveCount => Volatile.Read(ref _activeCount);

    /// <summary>
    /// Persists the job's initial state and enqueues it. Thread-safe; callable
    /// from the recording thread or the UI thread.
    /// </summary>
    public void Enqueue(UploadJob job)
    {
        job.Save(_logger);
        Interlocked.Increment(ref _activeCount);
        RaiseActiveCount();

        if (!_channel.Writer.TryWrite(job))
        {
            Interlocked.Decrement(ref _activeCount);
            RaiseActiveCount();
            _logger.LogWarning("Upload queue is closed; dropping job for {Path}", job.ClipLocalMp4Path);
        }
    }

    /// <summary>
    /// Scans the output directory for sidecars left behind by an interrupted run
    /// (anything not Done) and re-enqueues them. Returns the count re-enqueued.
    /// Used at startup (orphan recovery) and by the "Retry failed uploads" menu.
    /// </summary>
    public int RescanAndEnqueueIncomplete(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory)) return 0;

        int n = 0;
        foreach (var path in Directory.EnumerateFiles(outputDirectory, "*.upload.json"))
        {
            var job = UploadJob.TryLoad(path, _logger);
            if (job is null) continue;

            if (job.State == UploadState.Done)
            {
                job.DeleteSidecar(_logger); // stale success record
                continue;
            }

            if (!File.Exists(job.ClipLocalMp4Path))
            {
                _logger.LogWarning("Dropping orphaned upload — MP4 missing: {Path}", job.ClipLocalMp4Path);
                job.DeleteSidecar(_logger);
                continue;
            }

            // The poster may have been cleaned up; upload without it if so.
            if (job.ClipLocalJpgPath is { } jpg && !File.Exists(jpg))
            {
                job.ClipLocalJpgPath = null;
                job.JpgSizeBytes = null;
            }

            // Restart cleanly: fresh presigned URLs every attempt anyway.
            job.State = UploadState.Pending;
            job.AttemptCount = 0;
            Enqueue(job);
            n++;
        }

        if (n > 0) _logger.LogInformation("Re-enqueued {N} interrupted upload(s) from {Dir}", n, outputDirectory);
        return n;
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var job in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await ProcessJobAsync(job, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Upload consumer stopping (shutdown requested)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload consumer terminated unexpectedly");
        }
    }

    private async Task ProcessJobAsync(UploadJob job, CancellationToken ct)
    {
        try
        {
            try { _callbacks.Started?.Invoke(job); }
            catch (Exception ex) { _logger.LogError(ex, "Started handler threw"); }

            for (int attempt = 0; ; attempt++)
            {
                job.AttemptCount = attempt + 1;
                try
                {
                    await RunPipelineAsync(job, ct).ConfigureAwait(false);

                    job.State = UploadState.Done;
                    job.LastError = null;
                    job.DeleteSidecar(_logger);
                    _logger.LogInformation("Upload complete: clip {ClipId} → {Url}", job.ClipId, job.ViewerUrl);
                    _callbacks.Succeeded?.Invoke(job);
                    return;
                }
                catch (OperationCanceledException)
                {
                    // Shutdown: leave the sidecar at its last saved state for restart.
                    throw;
                }
                catch (UploadException ux)
                {
                    job.LastError = ux.Message;

                    if (ux.IsRetryable && attempt < RetryDelays.Length)
                    {
                        var delay = RetryDelays[attempt];
                        _logger.LogWarning(
                            "Upload attempt {N} failed (retryable, HTTP {Status}): {Msg}. Retrying in {Delay}.",
                            attempt + 1, ux.StatusCode, ux.Message, delay);
                        job.Save(_logger);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                        continue;
                    }

                    job.State = UploadState.Failed;
                    job.Save(_logger);
                    _logger.LogError(
                        "Upload failed for {Path}: {Msg} (HTTP {Status}, retryable={Retry}, attempts={Attempts})",
                        job.ClipLocalMp4Path, ux.Message, ux.StatusCode, ux.IsRetryable, attempt + 1);
                    _callbacks.Failed?.Invoke(job);
                    return;
                }
                catch (Exception ex)
                {
                    job.LastError = ex.Message;
                    job.State = UploadState.Failed;
                    job.Save(_logger);
                    _logger.LogError(ex, "Upload failed (unexpected) for {Path}", job.ClipLocalMp4Path);
                    _callbacks.Failed?.Invoke(job);
                    return;
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeCount);
            RaiseActiveCount();
        }
    }

    private async Task RunPipelineAsync(UploadJob job, CancellationToken ct)
    {
        job.State = UploadState.InitiatingThenUploading;
        job.Save(_logger);

        var init = await _api.InitiateAsync(
            job.Mp4SizeBytes, "video/mp4", job.Width, job.Height, job.JpgSizeBytes, ct).ConfigureAwait(false);
        job.ClipId = init.Id;
        job.Save(_logger);

        await _api.PutFileAsync(
            init.UploadUrl, job.ClipLocalMp4Path, "video/mp4", ct,
            onBytesSent: sent =>
            {
                try { _callbacks.Progress?.Invoke(job, sent, job.Mp4SizeBytes); }
                catch (Exception ex) { _logger.LogDebug(ex, "Progress handler threw"); }
            }).ConfigureAwait(false);
        // Report a final 100% so the bar visibly completes before "done".
        try { _callbacks.Progress?.Invoke(job, job.Mp4SizeBytes, job.Mp4SizeBytes); } catch { /* ignore */ }

        // Thumbnail is best-effort: the backend marks the clip ready without it,
        // so a failed poster PUT must never fail the clip.
        if (job.ClipLocalJpgPath is { } jpg && init.ThumbnailUploadUrl is { } thumbUrl && File.Exists(jpg))
        {
            try
            {
                await _api.PutFileAsync(thumbUrl, jpg, "image/jpeg", ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Thumbnail upload failed for clip {ClipId}; continuing without poster", init.Id);
            }
        }

        job.State = UploadState.Completing;
        job.Save(_logger);

        var done = await _api.CompleteAsync(init.CompleteUrl, ct).ConfigureAwait(false);
        job.ViewerUrl = string.IsNullOrWhiteSpace(done.ViewerUrl) ? init.ViewerUrl : done.ViewerUrl;
    }

    private void RaiseActiveCount()
    {
        try { _callbacks.ActiveCountChanged?.Invoke(ActiveCount); }
        catch (Exception ex) { _logger.LogError(ex, "ActiveCountChanged handler threw"); }
    }

    /// <summary>
    /// Completes the queue and waits up to <paramref name="grace"/> for the
    /// in-flight job to finish. Anything still running is cancelled; its sidecar
    /// persists and resumes on the next launch.
    /// </summary>
    public async Task ShutdownAsync(TimeSpan grace)
    {
        if (_disposed) return;
        _channel.Writer.TryComplete();

        var finished = await Task.WhenAny(_consumer, Task.Delay(grace)).ConfigureAwait(false);
        if (finished != _consumer)
        {
            _logger.LogWarning(
                "Upload grace period ({Grace:n0}s) elapsed with work in flight; cancelling (resumes next launch)",
                grace.TotalSeconds);
            try { _cts.Cancel(); } catch { /* already disposed */ }
            try { await _consumer.ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogDebug(ex, "Consumer faulted during shutdown"); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _channel.Writer.TryComplete(); } catch { /* ignore */ }
        try { if (!_cts.IsCancellationRequested) _cts.Cancel(); } catch { /* ignore */ }
        try { _consumer.Wait(TimeSpan.FromSeconds(2)); } catch { /* best-effort */ }
        _cts.Dispose();
    }
}
