using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Extensions.Logging;
using WipShare.Client.Native;
using WipShare.Client.Settings;
using static WipShare.Client.Native.Win32Interop;

namespace WipShare.Client.Capture;

public enum SelectionState
{
    Idle,
    Dragging,
    Confirmed,
    Canceled,
}

public enum SelectionOutcome
{
    Canceled,
    TooSmall,
    Confirmed,
}

public sealed record SelectionResult(SelectionOutcome Outcome, SelectedRegion? Region, Rect SelectionDipRect)
{
    public static readonly SelectionResult Canceled = new(SelectionOutcome.Canceled, null, Rect.Empty);
    public static readonly SelectionResult TooSmall = new(SelectionOutcome.TooSmall, null, Rect.Empty);
    public static SelectionResult Confirm(SelectedRegion r, Rect dipRect) =>
        new(SelectionOutcome.Confirmed, r, dipRect);
}

/// <summary>
/// Full-virtual-screen overlay that lets the user drag out a rectangle. The
/// window instance survives selection — on Confirmed, ownership is handed to the
/// caller (via <see cref="OverlayWindow"/>) so <see cref="RecordingOverlay"/> can
/// take it over without a visible flash. On Canceled/TooSmall the window closes.
/// </summary>
public sealed class RegionSelector : IDisposable
{
    private readonly ILogger<RegionSelector> _logger;
    private Window? _window;
    private TaskCompletionSource<SelectionResult>? _tcs;
    private SelectionState _state = SelectionState.Idle;
    private bool _disposed;

    // Visual tree (built once, referenced from event handlers)
    private RectangleGeometry? _outerGeo;
    private RectangleGeometry? _innerGeo;
    private Rectangle? _border;
    private Border? _labelHost;
    private TextBlock? _labelText;

    // Drag state (DIPs in window-local space)
    private Point _dragStart;
    private Rect _currentDip = Rect.Empty;

    public Window? OverlayWindow => _window;

    public RegionSelector(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<RegionSelector>();
    }

    public Task<SelectionResult> SelectAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RegionSelector));
        if (_tcs != null) throw new InvalidOperationException("SelectAsync already in progress");

        _tcs = new TaskCompletionSource<SelectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _state = SelectionState.Idle;
        _window = BuildOverlayWindow();
        _window.Show();
        _window.Activate();
        Keyboard.Focus(_window);
        _logger.LogInformation("Region selector shown (vw={W} vh={H})",
            (int)SystemParameters.VirtualScreenWidth, (int)SystemParameters.VirtualScreenHeight);
        return _tcs.Task;
    }

    private Window BuildOverlayWindow()
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = true,
            Cursor = Cursors.Cross,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight,
            Title = "WipShare — select region",
            UseLayoutRounding = true,
        };

        var root = new Canvas { Background = Brushes.Transparent };

        // Backdrop: a Path with EvenOdd fill. Outer rect covers everything;
        // inner rect (zero-sized at start) punches a hole during the drag.
        _outerGeo = new RectangleGeometry(new Rect(0, 0, w.Width, w.Height));
        _innerGeo = new RectangleGeometry(new Rect(0, 0, 0, 0));
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(_outerGeo);
        group.Children.Add(_innerGeo);
        var backdrop = new Path
        {
            Fill = Brushes.Black,
            Opacity = AppSettings.BackdropOpacity,
            Data = group,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(backdrop, 0);
        Canvas.SetTop(backdrop, 0);
        root.Children.Add(backdrop);

        // 2px white selection border (hidden until drag begins)
        _border = new Rectangle
        {
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            SnapsToDevicePixels = true,
        };
        root.Children.Add(_border);

        // Dimensions label: black 70% pill with white SemiBold text
        _labelText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 16, // 12pt @ 96 DPI
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _labelHost = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb((byte)(0.7 * 255), 0, 0, 0)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Child = _labelText,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        root.Children.Add(_labelHost);

        w.Content = root;
        w.KeyDown += OnKeyDown;
        w.MouseLeftButtonDown += OnMouseLeftDown;
        w.MouseMove += OnMouseMove;
        w.MouseLeftButtonUp += OnMouseLeftUp;
        // No LostMouseCapture handler: it fires synchronously inside our own
        // ReleaseMouseCapture call in OnMouseLeftUp, which would race with the
        // Confirmed-state assignment. If the user genuinely loses capture
        // (alt-tab, etc.), Esc remains the cancel path.
        w.Closed += OnClosed;

        return w;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel();
        }
    }

    private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (_state != SelectionState.Idle) return;
        _state = SelectionState.Dragging;
        _dragStart = e.GetPosition(_window);
        _currentDip = new Rect(_dragStart, new Size(0, 0));
        _window!.CaptureMouse();
        _border!.Visibility = Visibility.Visible;
        _labelHost!.Visibility = Visibility.Visible;
        UpdateVisuals();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_state != SelectionState.Dragging) return;
        var p = e.GetPosition(_window);
        _currentDip = SelectorMath.MakeRect(_dragStart, p);
        UpdateVisuals();
    }

    private void OnMouseLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (_state != SelectionState.Dragging || _window == null) return;
        var dip = _currentDip;
        // Release capture before transitioning state so any reentrancy lands cleanly.
        _window.ReleaseMouseCapture();

        if (dip.Width < 1 || dip.Height < 1)
        {
            ResolveTooSmall();
            return;
        }

        SelectedRegion region;
        int physWidth, physHeight;
        try
        {
            (region, physWidth, physHeight) = ResolveSelection(dip);
        }
        catch (InvalidOperationException ex)
        {
            // Window already gone (race during teardown) — treat as cancel.
            _logger.LogWarning(ex, "ResolveSelection failed; treating as cancel");
            Cancel();
            return;
        }

        if (physWidth < AppSettings.MinRegionWidth || physHeight < AppSettings.MinRegionHeight)
        {
            ResolveTooSmall();
            return;
        }

        _state = SelectionState.Confirmed;
        _logger.LogInformation(
            "Selection confirmed: monitor=0x{Mon:X} local=({X},{Y}) physical={PW}x{PH} (dip={DW:F1}x{DH:F1})",
            region.Monitor.ToInt64(), region.X, region.Y, region.Width, region.Height,
            dip.Width, dip.Height);
        _tcs?.TrySetResult(SelectionResult.Confirm(region, dip));
        // NOTE: window is left open. Caller transitions it to RecordingOverlay.
    }

    /// <summary>Convert the DIP rect (in window-local coords) into a clamped, monitor-local physical-pixel region.</summary>
    private (SelectedRegion Region, int PhysWidth, int PhysHeight) ResolveSelection(Rect dip)
    {
        // 1. WPF DIPs → screen physical pixels via PointToScreen
        var pTL = _window!.PointToScreen(dip.TopLeft);
        var pBR = _window!.PointToScreen(dip.BottomRight);
        var screenRect = new Rect(pTL, pBR);

        // 2. Find target monitor by the rect's center
        var center = new POINT((int)Math.Round((pTL.X + pBR.X) / 2.0), (int)Math.Round((pTL.Y + pBR.Y) / 2.0));
        var hMonitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero)
            hMonitor = MonitorFromPoint(new POINT(0, 0), MONITOR_DEFAULTTOPRIMARY);

        var mi = MONITORINFO.New();
        if (!GetMonitorInfoW(hMonitor, ref mi))
            throw new InvalidOperationException("GetMonitorInfo failed for chosen monitor");

        // 3. Clamp to monitor + convert to monitor-local
        var (x, y, w, h) = SelectorMath.ClampToMonitorLocal(screenRect, mi.rcMonitor);
        return (new SelectedRegion(hMonitor, x, y, w, h), w, h);
    }

    private void UpdateVisuals()
    {
        _innerGeo!.Rect = _currentDip;

        Canvas.SetLeft(_border!, _currentDip.X);
        Canvas.SetTop(_border!, _currentDip.Y);
        _border!.Width = Math.Max(0, _currentDip.Width);
        _border!.Height = Math.Max(0, _currentDip.Height);

        if (_currentDip.Width >= 1 && _currentDip.Height >= 1)
            UpdateDimensionsLabel();
    }

    private void UpdateDimensionsLabel()
    {
        // Probe the target monitor + its DPI to display physical-pixel dimensions.
        var pTL = _window!.PointToScreen(_currentDip.TopLeft);
        var pBR = _window!.PointToScreen(_currentDip.BottomRight);
        int physW = (int)Math.Round(Math.Abs(pBR.X - pTL.X));
        int physH = (int)Math.Round(Math.Abs(pBR.Y - pTL.Y));
        _labelText!.Text = $"{physW} × {physH}";

        // Position: 12px outside bottom-right by default; flip inside if it'd clip the virtual screen.
        _labelHost!.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var labelSz = _labelHost.DesiredSize;
        var (lx, ly) = SelectorMath.PositionLabel(
            _currentDip, labelSz,
            virtualWidth: _window.ActualWidth,
            virtualHeight: _window.ActualHeight,
            padding: 12);
        Canvas.SetLeft(_labelHost, lx);
        Canvas.SetTop(_labelHost, ly);
    }

    private void ResolveTooSmall()
    {
        _state = SelectionState.Canceled;
        _logger.LogInformation("Selection released but below minimum {MinW}x{MinH}",
            AppSettings.MinRegionWidth, AppSettings.MinRegionHeight);
        _tcs?.TrySetResult(SelectionResult.TooSmall);
        try { _window?.Close(); } catch { }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_tcs is { Task.IsCompleted: false })
        {
            _state = SelectionState.Canceled;
            _tcs.TrySetResult(SelectionResult.Canceled);
        }
    }

    private void Cancel()
    {
        if (_state is SelectionState.Confirmed or SelectionState.Canceled) return;
        _state = SelectionState.Canceled;
        _logger.LogInformation("Region selection canceled by user");
        _tcs?.TrySetResult(SelectionResult.Canceled);
        try { _window?.Close(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_state == SelectionState.Idle || _state == SelectionState.Dragging)
        {
            try { _window?.Close(); } catch { }
        }
        _window = null;
    }
}

/// <summary>
/// Pure functions used by <see cref="RegionSelector"/>. Broken out as statics so
/// they're trivially unit-testable without spinning up a WPF window.
/// </summary>
internal static class SelectorMath
{
    public static Rect MakeRect(Point a, Point b)
    {
        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);
        double w = Math.Abs(a.X - b.X);
        double h = Math.Abs(a.Y - b.Y);
        return new Rect(x, y, w, h);
    }

    /// <summary>
    /// Clamps a screen-pixel rectangle to the given monitor's bounds, then
    /// converts the result to monitor-local coordinates (0-based from the
    /// monitor's top-left). Returns (x, y, width, height).
    /// </summary>
    public static (int X, int Y, int Width, int Height) ClampToMonitorLocal(Rect screenRect, Win32Interop.RECT monitorRect)
    {
        int left   = (int)Math.Round(screenRect.X);
        int top    = (int)Math.Round(screenRect.Y);
        int right  = (int)Math.Round(screenRect.X + screenRect.Width);
        int bottom = (int)Math.Round(screenRect.Y + screenRect.Height);

        int clampedLeft   = Math.Max(left,   monitorRect.Left);
        int clampedTop    = Math.Max(top,    monitorRect.Top);
        int clampedRight  = Math.Min(right,  monitorRect.Right);
        int clampedBottom = Math.Min(bottom, monitorRect.Bottom);

        int localX = clampedLeft - monitorRect.Left;
        int localY = clampedTop  - monitorRect.Top;
        int localW = Math.Max(0, clampedRight  - clampedLeft);
        int localH = Math.Max(0, clampedBottom - clampedTop);

        return (localX, localY, localW, localH);
    }

    /// <summary>
    /// Positions the dimensions label 12px outside the bottom-right of the
    /// selection rectangle, or 12px inside it if that would clip past the
    /// virtual screen.
    /// </summary>
    public static (double X, double Y) PositionLabel(Rect selection, Size labelSize, double virtualWidth, double virtualHeight, double padding)
    {
        // Outside placement
        double x = selection.Right + padding;
        double y = selection.Bottom + padding;

        if (x + labelSize.Width > virtualWidth)
            x = selection.Right - labelSize.Width - padding;
        if (y + labelSize.Height > virtualHeight)
            y = selection.Bottom - labelSize.Height - padding;

        // Final guards so the label is never off the left/top edge either
        x = Math.Max(0, x);
        y = Math.Max(0, y);
        return (x, y);
    }
}
