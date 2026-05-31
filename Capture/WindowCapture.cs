using Microsoft.Extensions.Logging;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WipShare.Client.Native;
using WipShare.Client.Settings;

namespace WipShare.Client.Capture;

/// <summary>
/// Owns a single Windows.Graphics.Capture session for one HWND. Creates the D3D11
/// device, the GraphicsCaptureItem, a free-threaded frame pool, and the capture
/// session. Subscribers receive frames on a threadpool thread via FrameArrived.
/// </summary>
public sealed class WindowCapture : IDisposable
{
    private readonly ILogger<WindowCapture> _logger;
    private readonly IntPtr _hwnd;

    private IDirect3DDevice? _d3dDevice;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private bool _initialized;
    private bool _started;
    private bool _disposed;

    /// <summary>Resolution we asked the frame pool to deliver (after 1920×1080 capping).</summary>
    public SizeInt32 CaptureSize { get; private set; }

    /// <summary>Native size of the captured window before capping.</summary>
    public SizeInt32 ItemSize { get; private set; }

    /// <summary>
    /// Fires on a threadpool thread when a new frame is available. Handlers MUST
    /// pull the frame via <c>sender.TryGetNextFrame()</c> and dispose it after use.
    /// </summary>
    public event Action<Direct3D11CaptureFramePool>? FrameArrived;

    public WindowCapture(IntPtr hwnd, ILoggerFactory loggerFactory)
    {
        if (hwnd == IntPtr.Zero) throw new ArgumentException("HWND is null", nameof(hwnd));
        _hwnd = hwnd;
        _logger = loggerFactory.CreateLogger<WindowCapture>();
    }

    /// <summary>
    /// Allocates the D3D device, capture item, frame pool, and session. Safe to
    /// call before subscribers attach to <see cref="FrameArrived"/>. No frames are
    /// emitted until <see cref="Start"/> is called.
    /// </summary>
    public void Initialize()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowCapture));
        if (_initialized) return;

        _d3dDevice = D3D11Interop.CreateDirect3DDevice();
        _item = Win32Interop.CreateCaptureItemForWindow(_hwnd);

        ItemSize = _item.Size;
        CaptureSize = CapToBounds(ItemSize, AppSettings.MaxWidth, AppSettings.MaxHeight);

        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _d3dDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 2,
            CaptureSize);
        _framePool.FrameArrived += OnFrameArrivedRaw;

        _session = _framePool.CreateCaptureSession(_item);

        // Win11 22H2+: hide the yellow capture border.
        if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
        {
            try { _session.IsBorderRequired = false; }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not set IsBorderRequired=false"); }
        }

        _initialized = true;
        _logger.LogInformation(
            "Capture initialized: item={ItemW}x{ItemH}, will deliver {CapW}x{CapH}",
            ItemSize.Width, ItemSize.Height, CaptureSize.Width, CaptureSize.Height);
    }

    /// <summary>
    /// Begins emitting frames via <see cref="FrameArrived"/>. Calls <see cref="Initialize"/>
    /// if it hasn't been called yet.
    /// </summary>
    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowCapture));
        if (!_initialized) Initialize();
        if (_started) return;

        _session!.StartCapture();
        _started = true;
        _logger.LogInformation("Capture started");
    }

    private void OnFrameArrivedRaw(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            FrameArrived?.Invoke(sender);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FrameArrived subscriber threw");
        }
    }

    private static SizeInt32 CapToBounds(SizeInt32 src, int maxW, int maxH)
    {
        if (src.Width <= 0 || src.Height <= 0)
            throw new InvalidOperationException($"Window has non-positive size {src.Width}x{src.Height}");

        if (src.Width <= maxW && src.Height <= maxH) return EnsureEven(src);

        double s = Math.Min((double)maxW / src.Width, (double)maxH / src.Height);
        return EnsureEven(new SizeInt32 { Width = (int)(src.Width * s), Height = (int)(src.Height * s) });
    }

    // H.264 / NV12 want even dimensions.
    private static SizeInt32 EnsureEven(SizeInt32 s) => new()
    {
        Width = Math.Max(2, s.Width - (s.Width & 1)),
        Height = Math.Max(2, s.Height - (s.Height & 1)),
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_framePool != null)
            _framePool.FrameArrived -= OnFrameArrivedRaw;

        try { _session?.Dispose(); } catch (Exception ex) { _logger.LogError(ex, "Disposing capture session"); }
        try { _framePool?.Dispose(); } catch (Exception ex) { _logger.LogError(ex, "Disposing frame pool"); }
        try { _d3dDevice?.Dispose(); } catch (Exception ex) { _logger.LogError(ex, "Disposing D3D device"); }

        _session = null;
        _framePool = null;
        _d3dDevice = null;
        _item = null;
        _logger.LogInformation("Capture disposed");
    }
}
