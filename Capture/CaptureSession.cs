using Microsoft.Extensions.Logging;
using Windows.Graphics.Capture;
using WipShare.Client.Encoding;
using WipShare.Client.Settings;

namespace WipShare.Client.Capture;

public sealed record RecordingResult(
    string OutputPath,
    int FramesEncoded,
    int FramesDropped,
    int Width,
    int Height,
    string? ThumbnailPath);

/// <summary>
/// Owns one end-to-end recording lifecycle: a <see cref="MonitorCapture"/> producing
/// monitor frames into a staging slot, plus a 30 Hz timer that pulls the latest
/// staged frame (or re-encodes the last one if nothing new arrived) and feeds the
/// <see cref="VideoEncoder"/>. Step 6: records the full monitor (no GPU crop yet).
/// Step 7: a region-sized staging texture in MonitorCapture will crop to the
/// selected region before the encoder sees it.
/// </summary>
public sealed class CaptureSession : IDisposable
{
    private readonly ILogger<CaptureSession> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SelectedRegion _region;
    private readonly string _outputPath;
    private readonly TimeSpan _duration;
    private readonly int _fps;
    private readonly uint _bitrate;

    private MonitorCapture? _capture;
    private VideoEncoder? _encoder;
    private volatile bool _paused;
    private bool _disposed;

    /// <summary>Pause feeding frames to the encoder. Recorded-time (the 15s budget) freezes.</summary>
    public void Pause() => _paused = true;
    /// <summary>Resume feeding frames; recorded-time continues from where it froze.</summary>
    public void Resume() => _paused = false;

    public CaptureSession(
        SelectedRegion region,
        string outputPath,
        TimeSpan duration,
        int fps,
        uint bitrate,
        ILoggerFactory loggerFactory)
    {
        if (region.Monitor == IntPtr.Zero) throw new ArgumentException("region.Monitor is null", nameof(region));
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps));

        _region = region;
        _outputPath = outputPath;
        _duration = duration;
        _fps = fps;
        _bitrate = bitrate;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<CaptureSession>();
    }

    /// <param name="elapsedProgress">
    /// Optional live progress sink (recorded time so far). Reported every tick
    /// (~30 Hz) so the floating pill's timer + progress line update smoothly.
    /// Marshal to the UI thread in the handler (a <see cref="Progress{T}"/> does this).
    /// </param>
    /// <param name="finishToken">
    /// "Finish early" signal. When triggered, the capture loop stops gracefully
    /// and the clip is finalized + kept (unlike <paramref name="ct"/>, which the
    /// caller treats as a discard). Lets the user stop before the 15s budget.
    /// </param>
    /// <param name="restartToken">
    /// "Restart" signal. Breaks the loop like a finish; the caller discards the
    /// partial file and re-enters the countdown for the same region.
    /// </param>
    public async Task<RecordingResult> RunAsync(
        CancellationToken ct, IProgress<TimeSpan>? elapsedProgress = null,
        CancellationToken finishToken = default, CancellationToken restartToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CaptureSession));

        // Linked CTS: break the capture loop early on discard-cancel (ct), a
        // graceful finish (finishToken), a restart (restartToken), or if the
        // capture item closes (monitor disconnect). All fall through to
        // FinalizeAndSave; keep-vs-discard is the caller's decision, not ours.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, finishToken, restartToken);
        var loopToken = linkedCts.Token;

        _capture = new MonitorCapture(_region.Monitor, _region, _loggerFactory);
        _capture.ItemClosed += () =>
        {
            _logger.LogWarning("Monitor went away mid-recording — finalizing what's been written so far");
            linkedCts.Cancel();
        };
        await Task.Run(() => _capture.Initialize(), ct).ConfigureAwait(false);

        // Source dimensions are the cropped region (or the full monitor when
        // no crop is set). The encoder will scale-to-fit if these exceed
        // MaxWidth/MaxHeight, preserving aspect ratio.
        int sourceW = _capture.OutputSize.Width;
        int sourceH = _capture.OutputSize.Height;

        _encoder = new VideoEncoder(
            _outputPath,
            sourceW, sourceH,
            AppSettings.MaxWidth, AppSettings.MaxHeight,
            _fps, _bitrate, _loggerFactory);
        _encoder.Start();

        // The clip's true pixel dimensions (after the encoder's aspect-preserving
        // fit + even rounding) — reported to the backend for og:video/og:image.
        int outputW = _encoder.OutputWidth;
        int outputH = _encoder.OutputHeight;

        // Poster thumbnail: retain a COPY of the BGRA frame near the ~1s mark
        // (PosterFrameIndex). CropToBgraBytes hands back a reused buffer, so we
        // must copy. If the recording ends sooner, we keep the last frame copied.
        byte[]? posterBgra = null;
        int posterW = 0, posterH = 0;

        // Staging slot: producer (capture event) swaps `staged`, consumer
        // (timer) drains it. `lastEncoded` keeps the most recently encoded
        // frame alive so we can re-emit on idle ticks.
        Direct3D11CaptureFrame? staged = null;
        Direct3D11CaptureFrame? lastEncoded = null;
        var stagingLock = new object();

        _capture.FrameArrived += pool =>
        {
            var fresh = pool.TryGetNextFrame();
            if (fresh == null) return;
            Direct3D11CaptureFrame? dropped;
            lock (stagingLock)
            {
                dropped = staged;
                staged = fresh;
            }
            dropped?.Dispose();
        };

        _capture.Start();
        _logger.LogInformation(
            "Recording starting: {Duration}s @ {Fps} fps, source {SW}x{SH} → '{Path}'",
            _duration.TotalSeconds, _fps, sourceW, sourceH, _outputPath);

        var totalFrames = (int)Math.Round(_duration.TotalSeconds * _fps);
        var tickInterval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / _fps);
        using var timer = new PeriodicTimer(tickInterval);

        int encoded = 0;
        int dropped = 0;
        elapsedProgress?.Report(TimeSpan.Zero);

        try
        {
            // Budget is measured in FRAMES WRITTEN (recorded-time), not ticks, so
            // paused time never counts toward the 15s. The encoder timestamps each
            // sample by frame index, so simply not writing while paused keeps the
            // output timeline continuous — no frozen gap, no offset bookkeeping.
            while (encoded < totalFrames)
            {
                bool moreTicks;
                try { moreTicks = await timer.WaitForNextTickAsync(loopToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                if (!moreTicks) break;

                if (_paused) continue;  // freeze: write nothing, advance nothing

                Direct3D11CaptureFrame? frameToEncode;
                Direct3D11CaptureFrame? previousToDispose = null;
                lock (stagingLock)
                {
                    if (staged != null)
                    {
                        previousToDispose = lastEncoded;
                        lastEncoded = staged;
                        staged = null;
                    }
                    frameToEncode = lastEncoded;
                }
                previousToDispose?.Dispose();

                if (frameToEncode == null)
                {
                    dropped++;
                    continue;
                }

                try
                {
                    // GPU-crop into a staging texture, then CPU-readback the cropped
                    // region. Bytes are tightly packed (row stride == width * 4) and
                    // owned by MonitorCapture's reusable buffer.
                    var bgra = _capture.CropToBgraBytes(frameToEncode.Surface);
                    await _encoder.WriteFrameAsync(bgra, loopToken).ConfigureAwait(false);
                    encoded++;

                    // Live elapsed = recorded time so far (frames written / fps);
                    // freezes during pause because no frames are written.
                    elapsedProgress?.Report(
                        TimeSpan.FromSeconds(Math.Min(_duration.TotalSeconds, (double)encoded / _fps)));

                    // Snapshot the poster frame. `bgra` is the reused readback
                    // buffer (valid until the next CropToBgraBytes), so copy now.
                    // Copy up to PosterFrameIndex so a short clip still yields a
                    // poster (the last frame), and the ~1s frame otherwise.
                    if (encoded <= AppSettings.PosterFrameIndex)
                    {
                        int posterBytes = sourceW * sourceH * 4;
                        if (bgra.Length >= posterBytes)
                        {
                            posterBgra ??= new byte[posterBytes];
                            bgra.Span.Slice(0, posterBytes).CopyTo(posterBgra);
                            posterW = sourceW;
                            posterH = sourceH;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Encode failed at frame {N}", encoded + dropped);
                    dropped++;
                }
            }
        }
        finally
        {
            _capture.Dispose();
            lock (stagingLock)
            {
                if (staged != null && !ReferenceEquals(staged, lastEncoded))
                    staged.Dispose();
                lastEncoded?.Dispose();
                staged = null;
                lastEncoded = null;
            }

            try { _encoder.FinalizeAndSave(); }
            catch (Exception ex) { _logger.LogError(ex, "FinalizeAndSave failed"); }
        }

        // Best-effort poster JPEG sibling to the MP4. A failure here must never
        // sink the recording — TryGenerate logs and returns false.
        string? thumbnailPath = null;
        if (posterBgra != null && posterW > 0 && posterH > 0)
        {
            var jpgPath = AppSettings.ThumbnailPathFor(_outputPath);
            if (ThumbnailGenerator.TryGenerate(posterBgra, posterW, posterH, jpgPath, _logger))
                thumbnailPath = jpgPath;
        }
        else
        {
            _logger.LogWarning("No poster frame captured; thumbnail skipped for {Path}", _outputPath);
        }

        _logger.LogInformation(
            "Recording done: {Encoded}/{Total} frames encoded ({Dropped} ticks dropped), clip {OW}x{OH}, thumb={Thumb} → '{Path}'",
            encoded, totalFrames, dropped, outputW, outputH, thumbnailPath is not null, _outputPath);

        return new RecordingResult(_outputPath, encoded, dropped, outputW, outputH, thumbnailPath);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _capture?.Dispose(); } catch (Exception ex) { _logger.LogError(ex, "Capture dispose"); }
        try { _encoder?.Dispose(); } catch (Exception ex) { _logger.LogError(ex, "Encoder dispose"); }
        _capture = null;
        _encoder = null;
    }
}
