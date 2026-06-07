using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WipShare.Client.Native;
using static WipShare.Client.Native.D3D11Interop;

namespace WipShare.Client.Capture;

/// <summary>
/// Owns a single Windows.Graphics.Capture session for an entire HMONITOR,
/// plus (optionally) a region-sized CPU-readable staging texture that the GPU
/// crops each captured frame into via CopySubresourceRegion. The CPU readback
/// then walks just the region, not the whole monitor.
///
/// We use D3D11_USAGE_STAGING + D3D11_CPU_ACCESS_READ on the staging texture so
/// we can Map it directly into managed memory - that's bulletproof, whereas the
/// "wrap-as-IDirect3DSurface then let SoftwareBitmap.CreateCopyFromSurfaceAsync
/// read it" path turned out to produce subtly corrupt MP4s. CPU pixel readback
/// goes through Map → row-pitch-aware memcpy → byte[] handed to the encoder.
/// </summary>
public sealed class MonitorCapture : IDisposable
{
    private readonly ILogger<MonitorCapture> _logger;
    private readonly IntPtr _hMonitor;
    private readonly SelectedRegion? _crop;

    private D3D11Bundle? _bundle;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;

    // Staging path (only allocated when _crop != null)
    private IntPtr _pStagingTexture;
    private byte[]? _readbackBuffer;     // reusable, tightly packed BGRA
    private readonly object _cropLock = new();

    private bool _initialized;
    private bool _started;
    private bool _disposed;

    /// <summary>Native monitor resolution (the framepool's buffer size).</summary>
    public SizeInt32 MonitorSize { get; private set; }

    /// <summary>Output dimensions delivered to subscribers - equals the crop
    /// region size if cropping, else <see cref="MonitorSize"/>.</summary>
    public SizeInt32 OutputSize { get; private set; }

    public event Action<Direct3D11CaptureFramePool>? FrameArrived;

    /// <summary>
    /// Fires when the underlying <see cref="GraphicsCaptureItem"/> reports
    /// Closed - typically because the monitor was disconnected. Caller should
    /// finalize the recording gracefully.
    /// </summary>
    public event Action? ItemClosed;

    public MonitorCapture(IntPtr hMonitor, SelectedRegion? cropRegion, ILoggerFactory loggerFactory)
    {
        if (hMonitor == IntPtr.Zero) throw new ArgumentException("HMONITOR is null", nameof(hMonitor));
        _hMonitor = hMonitor;
        _crop = cropRegion;
        _logger = loggerFactory.CreateLogger<MonitorCapture>();
    }

    public void Initialize()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MonitorCapture));
        if (_initialized) return;

        _bundle = CreateD3D11Bundle();
        _item = Win32Interop.CreateCaptureItemForMonitor(_hMonitor);
        _item.Closed += OnItemClosed;
        MonitorSize = _item.Size;

        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _bundle.WinRTDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 2,
            MonitorSize);
        _framePool.FrameArrived += OnFrameArrivedRaw;

        _session = _framePool.CreateCaptureSession(_item);

        if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
        {
            try { _session.IsBorderRequired = false; }
            catch (Exception ex) { _logger.LogDebug(ex, "IsBorderRequired=false rejected"); }
        }

        if (_crop is { } crop)
        {
            int regionW = Math.Clamp(crop.Width,  1, MonitorSize.Width  - crop.X);
            int regionH = Math.Clamp(crop.Height, 1, MonitorSize.Height - crop.Y);
            // Round the crop DOWN to even dimensions. The downstream H.264/NV12
            // encoder requires even width/height; it packs each frame at
            // stride = width*4. If we handed it an ODD-width readback buffer, the
            // encoder would shrink the width to width-1 yet keep consuming our
            // width*4 packed rows - sliding every row 1px left of the one above,
            // i.e. a diagonal shear across the whole clip. Even dimensions keep
            // our packed stride and the encoder's stride identical → no skew.
            // Cost: at most a 1px crop on the right/bottom edge of odd selections.
            regionW = Math.Max(2, regionW & ~1);
            regionH = Math.Max(2, regionH & ~1);
            AllocateStaging(regionW, regionH);
            OutputSize = new SizeInt32 { Width = regionW, Height = regionH };
        }
        else
        {
            // Full-monitor fallback (legacy path): even-align for the same reason.
            OutputSize = new SizeInt32
            {
                Width  = Math.Max(2, MonitorSize.Width  & ~1),
                Height = Math.Max(2, MonitorSize.Height & ~1),
            };
        }

        _initialized = true;
        _logger.LogInformation(
            "MonitorCapture initialized: hMonitor=0x{Mon:X} monitor={MW}x{MH} → output={OW}x{OH} (crop={Crop})",
            _hMonitor.ToInt64(), MonitorSize.Width, MonitorSize.Height,
            OutputSize.Width, OutputSize.Height,
            _crop is null ? "none" : $"({_crop.Value.X},{_crop.Value.Y})+{_crop.Value.Width}x{_crop.Value.Height}");
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MonitorCapture));
        if (!_initialized) Initialize();
        if (_started) return;

        _session!.StartCapture();
        _started = true;
        _logger.LogInformation("MonitorCapture started");
    }

    /// <summary>
    /// Copies the cropped region from <paramref name="source"/> into the
    /// staging texture, Maps it, and returns the BGRA bytes tightly packed
    /// (row stride == OutputSize.Width * 4). Returns the same backing byte[]
    /// every call - callers must consume it before the next call.
    ///
    /// Must be called serially from a consumer loop.
    /// </summary>
    public unsafe ReadOnlyMemory<byte> CropToBgraBytes(IDirect3DSurface source)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MonitorCapture));
        if (_crop is null || _pStagingTexture == IntPtr.Zero || _readbackBuffer is null)
            throw new InvalidOperationException("CropToBgraBytes requires a crop region; use a different path for full-monitor capture");

        var crop = _crop.Value;
        IntPtr pSource = GetTextureFromSurface(source);
        try
        {
            var box = new D3D11_BOX
            {
                left   = (uint)crop.X,
                top    = (uint)crop.Y,
                right  = (uint)(crop.X + OutputSize.Width),
                bottom = (uint)(crop.Y + OutputSize.Height),
                front  = 0,
                back   = 1,
            };

            lock (_cropLock)
            {
                ContextCopySubresourceRegion(
                    _bundle!.RawContext,
                    _pStagingTexture, 0, 0, 0, 0,
                    pSource, 0, box);

                int hr = ContextMap(_bundle.RawContext, _pStagingTexture, 0, D3D11_MAP_READ, 0, out var mapped);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                try
                {
                    int width = OutputSize.Width;
                    int height = OutputSize.Height;
                    int packedStride = width * 4;
                    byte* src = (byte*)mapped.pData;
                    int rowPitch = (int)mapped.RowPitch;

                    fixed (byte* dst = _readbackBuffer)
                    {
                        if (rowPitch == packedStride)
                        {
                            System.Buffer.MemoryCopy(src, dst, _readbackBuffer.Length, (long)packedStride * height);
                        }
                        else
                        {
                            // Map can return padded rows; copy row by row.
                            for (int y = 0; y < height; y++)
                                System.Buffer.MemoryCopy(src + y * rowPitch, dst + y * packedStride, packedStride, packedStride);
                        }
                    }
                }
                finally
                {
                    ContextUnmap(_bundle.RawContext, _pStagingTexture, 0);
                }
            }

            return _readbackBuffer.AsMemory(0, OutputSize.Width * OutputSize.Height * 4);
        }
        finally
        {
            Marshal.Release(pSource);
        }
    }

    private void AllocateStaging(int width, int height)
    {
        // STAGING + CPU_ACCESS_READ: we map this directly to read pixels. No
        // bind flags - staging-usage textures can't bind to the pipeline.
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width  = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = D3D11_USAGE_STAGING,
            BindFlags = 0,
            CPUAccessFlags = D3D11_CPU_ACCESS_READ,
            MiscFlags = 0,
        };
        int hr = DeviceCreateTexture2D(_bundle!.RawDevice, desc, out _pStagingTexture);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);

        _readbackBuffer = new byte[width * height * 4];
    }

    private void OnFrameArrivedRaw(Direct3D11CaptureFramePool sender, object args)
    {
        try { FrameArrived?.Invoke(sender); }
        catch (Exception ex) { _logger.LogError(ex, "FrameArrived subscriber threw"); }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        _logger.LogWarning("Capture item closed (monitor disconnected or display reset)");
        try { ItemClosed?.Invoke(); }
        catch (Exception ex) { _logger.LogError(ex, "ItemClosed subscriber threw"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_framePool != null) _framePool.FrameArrived -= OnFrameArrivedRaw;
        if (_item != null)      _item.Closed -= OnItemClosed;
        try { _session?.Dispose(); }    catch (Exception ex) { _logger.LogError(ex, "Disposing session"); }
        try { _framePool?.Dispose(); }  catch (Exception ex) { _logger.LogError(ex, "Disposing frame pool"); }
        if (_pStagingTexture != IntPtr.Zero) { Marshal.Release(_pStagingTexture); _pStagingTexture = IntPtr.Zero; }
        try { _bundle?.Dispose(); } catch (Exception ex) { _logger.LogError(ex, "Disposing D3D11 bundle"); }

        _session = null;
        _framePool = null;
        _bundle = null;
        _item = null;
        _readbackBuffer = null;
        _logger.LogInformation("MonitorCapture disposed");
    }
}
