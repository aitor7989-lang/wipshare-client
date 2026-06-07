using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Microsoft.Extensions.Logging;
using WipShare.Client.Native;
using WipShare.Client.Settings;

namespace WipShare.Client.Capture;

/// <summary>
/// Takes over the <see cref="RegionSelector"/>'s window for the countdown +
/// recording. This is the design's "locked" region-overlay state: the dim stays
/// (eased lighter to <see cref="AppSettings.LockedBackdropOpacity"/>) as the
/// recording-region frame - a white 1px border with a faint black outer edge and
/// white corner ticks around the live region - so finishing the drag never makes
/// the dim flash off. The window is click-through and excluded from screen capture
/// so it never films itself or the dim. The recording-state signal (timer,
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
        // Rebuild the window's content as the locked frame: the dim stays (lighter)
        // with the selection punched out, plus a white border + corner ticks.
        var root = new Canvas
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };

        double vw = _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;
        double vh = _window.ActualHeight > 0 ? _window.ActualHeight : _window.Height;
        var r = _selectionDipRect;

        // Dim cut-out, eased to the lighter locked opacity. EvenOdd fill = full
        // screen minus the selection hole.
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry(new Rect(0, 0, vw, vh)));
        group.Children.Add(new RectangleGeometry(r));
        root.Children.Add(new Path
        {
            Fill = new SolidColorBrush(Color.FromRgb(6, 7, 8)),
            Opacity = AppSettings.LockedBackdropOpacity,
            Data = group,
            IsHitTestVisible = false,
        });

        // 1px black outer edge + 1px white border (reads on any content).
        root.Children.Add(EdgeRect(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2,
            new SolidColorBrush(Color.FromArgb(140, 0, 0, 0))));
        root.Children.Add(EdgeRect(r.X, r.Y, r.Width, r.Height, Brushes.White));

        // White corner ticks.
        AddCorner(root, new Thickness(2, 2, 0, 0), new CornerRadius(3, 0, 0, 0), r.X - 1, r.Y - 1);
        AddCorner(root, new Thickness(0, 2, 2, 0), new CornerRadius(0, 3, 0, 0), r.Right + 1 - 12, r.Y - 1);
        AddCorner(root, new Thickness(2, 0, 0, 2), new CornerRadius(0, 0, 0, 3), r.X - 1, r.Bottom + 1 - 12);
        AddCorner(root, new Thickness(0, 0, 2, 2), new CornerRadius(0, 0, 3, 0), r.Right + 1 - 12, r.Bottom + 1 - 12);

        _window.Content = root;
        _window.Cursor = Cursors.Arrow;

        // Win32-level click-through and capture exclusion. WPF's IsHitTestVisible
        // alone leaves the WS_EX layer catching mouse messages; WS_EX_TRANSPARENT
        // makes the OS skip the window entirely during hit testing, so clicks
        // land on the app underneath. EXCLUDEFROMCAPTURE keeps the dim + frame out
        // of the recorded clip.
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var current = Win32Interop.GetWindowLongPtr(hwnd, Win32Interop.GWL_EXSTYLE).ToInt64();
            var updated = (IntPtr)(current | Win32Interop.WS_EX_TRANSPARENT | Win32Interop.WS_EX_LAYERED | Win32Interop.WS_EX_NOACTIVATE);
            Win32Interop.SetWindowLongPtr(hwnd, Win32Interop.GWL_EXSTYLE, updated);

            if (!Win32Interop.SetWindowDisplayAffinity(hwnd, Win32Interop.WDA_EXCLUDEFROMCAPTURE))
            {
                // Pre-2004 Windows 10 doesn't support EXCLUDEFROMCAPTURE - the
                // border will end up in the recording. Not fatal.
                _logger.LogWarning("SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) failed: {Err}",
                    Marshal.GetLastWin32Error());
            }
        }

        _logger.LogInformation(
            "Recording overlay engaged (locked frame): border at ({X:F1},{Y:F1}) {W:F1}x{H:F1}",
            r.X, r.Y, r.Width, r.Height);
    }

    private static Rectangle EdgeRect(double x, double y, double w, double h, Brush stroke)
    {
        var rect = new Rectangle
        {
            Stroke = stroke,
            StrokeThickness = 1,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
            Width = Math.Max(0, w),
            Height = Math.Max(0, h),
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        return rect;
    }

    private static void AddCorner(Canvas root, Thickness thickness, CornerRadius radius, double x, double y)
    {
        var c = new Border
        {
            Width = 12,
            Height = 12,
            BorderBrush = Brushes.White,
            BorderThickness = thickness,
            CornerRadius = radius,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = new DropShadowEffect { BlurRadius = 1, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.55 },
        };
        Canvas.SetLeft(c, x);
        Canvas.SetTop(c, y);
        root.Children.Add(c);

        // One-shot "pulse on lock" (design: corners tick to 1.3× and settle).
        var st = new ScaleTransform(1, 1);
        c.RenderTransform = st;
        var pulse = new DoubleAnimation(1, 1.3, TimeSpan.FromMilliseconds(180))
        {
            AutoReverse = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    /// <summary>
    /// Closes the overlay window. Idempotent - safe to call multiple times.
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
