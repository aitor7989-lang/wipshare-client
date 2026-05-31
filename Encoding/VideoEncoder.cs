using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WipShare.Client.Native;
using static WipShare.Client.Native.MediaFoundationInterop;

namespace WipShare.Client.Encoding;

/// <summary>
/// Media Foundation SinkWriter pipeline. Configured for H.264 Main @ a target bitrate,
/// MP4 container, no audio. Input is BGRA (MFVideoFormat_ARGB32); the sink writer
/// inserts a color converter MFT to land at NV12 for the H.264 encoder.
/// </summary>
public sealed class VideoEncoder : IDisposable
{
    private readonly ILogger<VideoEncoder> _logger;
    private readonly string _outputPath;
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private readonly int _outputWidth;
    private readonly int _outputHeight;
    private readonly int _fps;
    private readonly uint _bitrate;
    private readonly long _frameDuration100Ns;
    private readonly uint _stride;
    private readonly uint _bufferSize;

    private IntPtr _pSinkWriter;
    private uint _streamIndex;
    private bool _writingStarted;
    private bool _finalized;
    private bool _disposed;
    private long _frameIndex;

    /// <summary>Actual encoded MP4 width in pixels (after aspect-preserving fit + even rounding).</summary>
    public int OutputWidth => _outputWidth;
    /// <summary>Actual encoded MP4 height in pixels (after aspect-preserving fit + even rounding).</summary>
    public int OutputHeight => _outputHeight;

    // Reusable per-frame staging — avoids per-frame GC churn.
    private Windows.Storage.Streams.Buffer? _stagingBuffer;
    private byte[]? _stagingBytes;

    /// <summary>
    /// Source dimensions are what the encoder receives per frame (BGRA pixels).
    /// Output dimensions are computed from <paramref name="maxOutputWidth"/> /
    /// <paramref name="maxOutputHeight"/> by aspect-preserving fit-to-bounds; if
    /// source is already within bounds, output == source. If different, the
    /// SinkWriter inserts a Video Processor MFT to scale the BGRA frames before
    /// they reach the H.264 encoder.
    /// </summary>
    public VideoEncoder(
        string outputPath,
        int sourceWidth, int sourceHeight,
        int maxOutputWidth, int maxOutputHeight,
        int fps, uint bitrate, ILoggerFactory loggerFactory)
    {
        if (sourceWidth <= 0) throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        if (sourceHeight <= 0) throw new ArgumentOutOfRangeException(nameof(sourceHeight));
        if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps));

        // Source must be even for the NV12 conversion downstream.
        _sourceWidth  = sourceWidth  - (sourceWidth  & 1);
        _sourceHeight = sourceHeight - (sourceHeight & 1);

        var (ow, oh) = ScaleToFitEven(_sourceWidth, _sourceHeight, maxOutputWidth, maxOutputHeight);
        _outputWidth  = ow;
        _outputHeight = oh;

        _outputPath = outputPath;
        _fps = fps;
        _bitrate = bitrate;
        _frameDuration100Ns = 10_000_000L / fps;
        _stride = (uint)(_sourceWidth * 4);
        _bufferSize = _stride * (uint)_sourceHeight;
        _logger = loggerFactory.CreateLogger<VideoEncoder>();
    }

    /// <summary>
    /// Scales (srcW, srcH) to fit within (maxW, maxH) preserving aspect ratio.
    /// Result is rounded down to the nearest even pixel (NV12 requires even dims).
    /// If source already fits, returns it unchanged (after evenness rounding).
    /// </summary>
    public static (int Width, int Height) ScaleToFitEven(int srcW, int srcH, int maxW, int maxH)
    {
        if (srcW <= maxW && srcH <= maxH) return (EvenDown(srcW), EvenDown(srcH));
        double scale = Math.Min((double)maxW / srcW, (double)maxH / srcH);
        return (EvenDown((int)(srcW * scale)), EvenDown((int)(srcH * scale)));

        static int EvenDown(int n) => Math.Max(2, n - (n & 1));
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(VideoEncoder));
        if (_writingStarted) return;

        Directory.CreateDirectory(Path.GetDirectoryName(_outputPath)!);

        // Container attributes — force MPEG-4 muxer regardless of .mp4 extension.
        IntPtr pContainerAttrs = IntPtr.Zero;
        try
        {
            int hr = MFCreateAttributes(out pContainerAttrs, 1);
            ThrowIfFailed(hr, nameof(MFCreateAttributes));
            ThrowIfFailed(AttrSetGuid(pContainerAttrs, MF_TRANSCODE_CONTAINERTYPE, MFTranscodeContainerType_MPEG4),
                "set MF_TRANSCODE_CONTAINERTYPE");

            hr = MFCreateSinkWriterFromURL(_outputPath, IntPtr.Zero, pContainerAttrs, out _pSinkWriter);
            ThrowIfFailed(hr, nameof(MFCreateSinkWriterFromURL));
        }
        finally
        {
            if (pContainerAttrs != IntPtr.Zero) Marshal.Release(pContainerAttrs);
        }

        // Configure output (H.264) and input (BGRA) media types.
        IntPtr pOutputType = CreateOutputType();
        IntPtr pInputType = IntPtr.Zero;
        try
        {
            int hr = SinkAddStream(_pSinkWriter, pOutputType, out _streamIndex);
            ThrowIfFailed(hr, "SinkWriter::AddStream");

            pInputType = CreateInputType();
            hr = SinkSetInputMediaType(_pSinkWriter, _streamIndex, pInputType, IntPtr.Zero);
            ThrowIfFailed(hr, "SinkWriter::SetInputMediaType");

            hr = SinkBeginWriting(_pSinkWriter);
            ThrowIfFailed(hr, "SinkWriter::BeginWriting");
            _writingStarted = true;

            _logger.LogInformation(
                "SinkWriter ready: src {SW}x{SH} → out {OW}x{OH} @ {Fps}fps, target {Mbps} Mbps → '{Path}'",
                _sourceWidth, _sourceHeight, _outputWidth, _outputHeight, _fps, _bitrate / 1_000_000.0, _outputPath);
        }
        finally
        {
            if (pOutputType != IntPtr.Zero) Marshal.Release(pOutputType);
            if (pInputType != IntPtr.Zero) Marshal.Release(pInputType);
        }
    }

    private IntPtr CreateOutputType()
    {
        int hr = MFCreateMediaType(out IntPtr pType);
        ThrowIfFailed(hr, "MFCreateMediaType(output)");
        try
        {
            ThrowIfFailed(AttrSetGuid(pType, MF_MT_MAJOR_TYPE, MFMediaType_Video), "out MAJOR_TYPE");
            ThrowIfFailed(AttrSetGuid(pType, MF_MT_SUBTYPE, MFVideoFormat_H264), "out SUBTYPE");
            ThrowIfFailed(AttrSetUInt32(pType, MF_MT_AVG_BITRATE, _bitrate), "out AVG_BITRATE");
            ThrowIfFailed(AttrSetUInt32(pType, MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive), "out INTERLACE_MODE");
            ThrowIfFailed(AttrSetUInt32(pType, MF_MT_MPEG2_PROFILE, eAVEncH264VProfile_Main), "out MPEG2_PROFILE");
            ThrowIfFailed(AttrSetUInt64(pType, MF_MT_FRAME_SIZE, PackU64((uint)_outputWidth, (uint)_outputHeight)), "out FRAME_SIZE");
            ThrowIfFailed(AttrSetUInt64(pType, MF_MT_FRAME_RATE, PackU64((uint)_fps, 1)), "out FRAME_RATE");
            ThrowIfFailed(AttrSetUInt64(pType, MF_MT_PIXEL_ASPECT_RATIO, PackU64(1, 1)), "out PIXEL_ASPECT_RATIO");
            return pType;
        }
        catch
        {
            Marshal.Release(pType);
            throw;
        }
    }

    private IntPtr CreateInputType()
    {
        int hr = MFCreateMediaType(out IntPtr pType);
        ThrowIfFailed(hr, "MFCreateMediaType(input)");
        try
        {
            ThrowIfFailed(AttrSetGuid(pType, MF_MT_MAJOR_TYPE, MFMediaType_Video), "in MAJOR_TYPE");
            ThrowIfFailed(AttrSetGuid(pType, MF_MT_SUBTYPE, MFVideoFormat_ARGB32), "in SUBTYPE");
            ThrowIfFailed(AttrSetUInt32(pType, MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive), "in INTERLACE_MODE");
            ThrowIfFailed(AttrSetUInt32(pType, MF_MT_DEFAULT_STRIDE, _stride), "in DEFAULT_STRIDE");
            ThrowIfFailed(AttrSetUInt64(pType, MF_MT_FRAME_SIZE, PackU64((uint)_sourceWidth, (uint)_sourceHeight)), "in FRAME_SIZE");
            ThrowIfFailed(AttrSetUInt64(pType, MF_MT_FRAME_RATE, PackU64((uint)_fps, 1)), "in FRAME_RATE");
            ThrowIfFailed(AttrSetUInt64(pType, MF_MT_PIXEL_ASPECT_RATIO, PackU64(1, 1)), "in PIXEL_ASPECT_RATIO");
            return pType;
        }
        catch
        {
            Marshal.Release(pType);
            throw;
        }
    }

    /// <summary>
    /// Hand a tightly-packed BGRA frame to the SinkWriter as the next sample
    /// with a monotonic timestamp. <paramref name="bgra"/> must be exactly
    /// <c>SourceWidth × SourceHeight × 4</c> bytes; row stride is implicit
    /// (no padding).
    /// </summary>
    public Task WriteFrameAsync(ReadOnlyMemory<byte> bgra, CancellationToken ct)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(VideoEncoder));
        if (!_writingStarted) throw new InvalidOperationException("Call Start() first");
        if (bgra.Length < _bufferSize)
            throw new ArgumentException($"BGRA buffer too small: {bgra.Length} < {_bufferSize}", nameof(bgra));

        ct.ThrowIfCancellationRequested();

        long time100ns = _frameIndex * _frameDuration100Ns;
        _frameIndex++;
        WriteSampleFromSpan(bgra.Span, time100ns, _frameDuration100Ns);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Legacy IDirect3DSurface path — retained for callers that still pass a
    /// captured Direct3D surface (e.g. the dead WindowCapture code path).
    /// Internally pulls BGRA bytes off the GPU via SoftwareBitmap and then
    /// delegates to the bytes path.
    /// </summary>
    public async Task WriteFrameAsync(IDirect3DSurface surface, CancellationToken ct)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(VideoEncoder));
        if (!_writingStarted) throw new InvalidOperationException("Call Start() first");

        var bytes = await ReadbackBgraAsync(surface).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        long time100ns = _frameIndex * _frameDuration100Ns;
        _frameIndex++;
        WriteSampleFromSpan(bytes.AsSpan(0, (int)_bufferSize), time100ns, _frameDuration100Ns);
    }

    private async Task<byte[]> ReadbackBgraAsync(IDirect3DSurface surface)
    {
        using var bmp = await SoftwareBitmap.CreateCopyFromSurfaceAsync(surface, BitmapAlphaMode.Premultiplied);
        _stagingBuffer ??= new Windows.Storage.Streams.Buffer(_bufferSize);
        _stagingBytes  ??= new byte[_bufferSize];
        bmp.CopyToBuffer(_stagingBuffer);
        var reader = DataReader.FromBuffer(_stagingBuffer);
        reader.ReadBytes(_stagingBytes);
        return _stagingBytes;
    }

    private unsafe void WriteSampleFromSpan(ReadOnlySpan<byte> bgra, long time100ns, long duration100ns)
    {
        int hr = MFCreateMemoryBuffer(_bufferSize, out IntPtr pBuffer);
        ThrowIfFailed(hr, nameof(MFCreateMemoryBuffer));
        try
        {
            hr = BufferLock(pBuffer, out IntPtr pDst, out _, out _);
            ThrowIfFailed(hr, "IMFMediaBuffer::Lock");
            try
            {
                fixed (byte* pSrc = bgra)
                {
                    System.Buffer.MemoryCopy(pSrc, (void*)pDst, _bufferSize, _bufferSize);
                }
            }
            finally { BufferUnlock(pBuffer); }
            ThrowIfFailed(BufferSetCurrentLength(pBuffer, _bufferSize), "IMFMediaBuffer::SetCurrentLength");

            hr = MFCreateSample(out IntPtr pSample);
            ThrowIfFailed(hr, nameof(MFCreateSample));
            try
            {
                ThrowIfFailed(SampleSetSampleTime(pSample, time100ns), "IMFSample::SetSampleTime");
                ThrowIfFailed(SampleSetSampleDuration(pSample, duration100ns), "IMFSample::SetSampleDuration");
                ThrowIfFailed(SampleAddBuffer(pSample, pBuffer), "IMFSample::AddBuffer");

                hr = SinkWriteSample(_pSinkWriter, _streamIndex, pSample);
                ThrowIfFailed(hr, "SinkWriter::WriteSample");
            }
            finally { Marshal.Release(pSample); }
        }
        finally { Marshal.Release(pBuffer); }
    }

    /// <summary>
    /// Finalizes the MP4 file. Safe to call exactly once. Subsequent calls are no-ops.
    /// </summary>
    public void FinalizeAndSave()
    {
        if (_finalized || _pSinkWriter == IntPtr.Zero) return;
        _finalized = true;

        int hr = SinkFinalize(_pSinkWriter);
        ThrowIfFailed(hr, "SinkWriter::Finalize");
        _logger.LogInformation("Finalized {Frames} frames → '{Path}'", _frameIndex, _outputPath);
    }

    private static void ThrowIfFailed(int hr, string where)
    {
        if (hr < 0)
        {
            var ex = Marshal.GetExceptionForHR(hr) ?? new COMException($"HRESULT 0x{(uint)hr:X8}", hr);
            throw new InvalidOperationException($"{where} failed (HRESULT 0x{(uint)hr:X8})", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_finalized && _writingStarted && _pSinkWriter != IntPtr.Zero)
        {
            // Best-effort: finalize so the MP4 isn't left truncated.
            try { SinkFinalize(_pSinkWriter); }
            catch (Exception ex) { _logger.LogError(ex, "Finalize on Dispose failed"); }
        }

        if (_pSinkWriter != IntPtr.Zero)
        {
            Marshal.Release(_pSinkWriter);
            _pSinkWriter = IntPtr.Zero;
        }
    }
}
