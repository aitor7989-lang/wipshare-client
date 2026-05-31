using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Extensions.Logging;
using WipShare.Client.Native;

namespace WipShare.Client.Capture;

/// <summary>
/// Takes over the <see cref="RegionSelector"/>'s window for the countdown +
/// recording. The dim backdrop disappears, leaving only a quiet 1px gray edge
/// around the selection. The window is click-through and excluded from screen
/// capture so it never films itself. The recording-state signal (timer,
/// progress, cancel) lives on the separate floating pill, not here.
/// </summary>
public sealed class RecordingOverlay : IDisposable
{
    private readonly ILogger<RecordingOverlay> _logger;
    private readonly Window _window;
    private readonly Rect _selectionDipRect;
    private bool _disposed;

    public RecordingOverlay(Window overlayWindow, Rect selectionDipRect, ILoggerFactory loggerFactory)
    {
        _window = overlayWindow ?? throw new ArgumentNullException(nameof(overlayWindow));
        _selectionDipRect = selectionDipRect;
        _logger = loggerFactory.CreateLogger<RecordingOverlay>();
    }

    public void Show()
    {
        // Rebuild the window's content: just the border + REC pip, no backdrop.
        var root = new Canvas
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };

        // Quiet 1px edge marking the captured rectangle — no label, no pip.
        // Gray (not white) per the established look; the floating pill now
        // carries the recording-state signal.
        var border = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
            StrokeThickness = 1,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
            Width = _selectionDipRect.Width,
            Height = _selectionDipRect.Height,
        };
        Canvas.SetLeft(border, _selectionDipRect.X);
        Canvas.SetTop(border, _selectionDipRect.Y);
        root.Children.Add(border);

        _window.Content = root;
        _window.Cursor = Cursors.Arrow;

        // Win32-level click-through and capture exclusion. WPF's IsHitTestVisible
        // alone leaves the WS_EX layer catching mouse messages; WS_EX_TRANSPARENT
        // makes the OS skip the window entirely during hit testing, so clicks
        // land on the app underneath.
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var current = Win32Interop.GetWindowLongPtr(hwnd, Win32Interop.GWL_EXSTYLE).ToInt64();
            var updated = (IntPtr)(current | Win32Interop.WS_EX_TRANSPARENT | Win32Interop.WS_EX_LAYERED | Win32Interop.WS_EX_NOACTIVATE);
            Win32Interop.SetWindowLongPtr(hwnd, Win32Interop.GWL_EXSTYLE, updated);

            if (!Win32Interop.SetWindowDisplayAffinity(hwnd, Win32Interop.WDA_EXCLUDEFROMCAPTURE))
            {
                // Pre-2004 Windows 10 doesn't support EXCLUDEFROMCAPTURE — the
                // border will end up in the recording. Not fatal.
                _logger.LogWarning("SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) failed: {Err}",
                    Marshal.GetLastWin32Error());
            }
        }

        _logger.LogInformation(
            "Recording overlay engaged: border at ({X:F1},{Y:F1}) {W:F1}x{H:F1}",
            _selectionDipRect.X, _selectionDipRect.Y, _selectionDipRect.Width, _selectionDipRect.Height);
    }

    /// <summary>
    /// Closes the overlay window. Idempotent — safe to call multiple times.
    /// </summary>
    public void Close()
    {
        // Intentionally NOT guarded by _disposed: Dispose sets that flag first
        // and then calls us, so guarding here would no-op the actual close.
        // Window.Close itself is idempotent.
        try { _window.Close(); }
        catch (Exception ex) { _logger.LogError(ex, "Closing recording overlay window"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }
}
