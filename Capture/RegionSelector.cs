using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
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
/// Full-virtual-screen overlay that lets the user drag out a rectangle, recreating
/// the design's region-overlay.html: idle crosshair guide-lines + an auto-fading
/// hint, a dim cut-out selection with a 1px white border (faint black outer edge),
/// white corner ticks, a frosted dimensions readout clamped on-screen, and a calm
/// inline "too small" message that re-arms. The window instance survives a confirmed
/// selection - ownership is handed to <see cref="RecordingOverlay"/> which keeps the
/// dim (eased lighter) as the locked recording-region frame, so it never flashes off.
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
    private Rectangle? _selFill;     // faint white wash inside the selection
    private Rectangle? _selWhite;    // 1px white border
    private Rectangle? _selBlack;    // 1px black outer edge (reads on light content)
    private Border? _cTL, _cTR, _cBL, _cBR;  // white corner ticks
    private Rectangle? _crossV, _crossH;     // idle crosshair guide-lines
    private Border? _labelHost;
    private TextBlock? _labelText;
    private Border? _hint;
    private Border? _tooSmallMsg;

    // Timers: the hint fades after idle; the too-small message clears itself.
    private DispatcherTimer? _hintFadeTimer;
    private DispatcherTimer? _tooSmallTimer;

    // Drag state (DIPs in window-local space)
    private Point _dragStart;
    private Rect _currentDip = Rect.Empty;

    public Window? OverlayWindow => _window;

    /// <summary>
    /// True while the overlay is still in its selection phase (shown, not yet
    /// confirmed or closed). A hotkey re-press during this window restarts the
    /// selection rather than being ignored or stacking a second overlay.
    /// </summary>
    public bool IsSelecting =>
        _tcs is { Task.IsCompleted: false } && _state is SelectionState.Idle or SelectionState.Dragging;

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
            Title = "WipShare - select region",
            UseLayoutRounding = true,
        };

        var root = new Canvas { Background = Brushes.Transparent };

        // Backdrop: a Path with EvenOdd fill. Outer rect covers everything; inner
        // rect (zero-sized at start) punches the selection hole during the drag.
        // Idle has no hole, so the whole screen reads dimmed.
        _outerGeo = new RectangleGeometry(new Rect(0, 0, w.Width, w.Height));
        _innerGeo = new RectangleGeometry(new Rect(0, 0, 0, 0));
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(_outerGeo);
        group.Children.Add(_innerGeo);
        var backdrop = new Path
        {
            Fill = new SolidColorBrush(Color.FromRgb(6, 7, 8)),
            Opacity = AppSettings.BackdropOpacity,
            Data = group,
            IsHitTestVisible = false,
        };
        root.Children.Add(backdrop);

        // Idle crosshair guide-lines (thin, ~15% white), tracking the cursor.
        var guideBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
        _crossV = new Rectangle { Width = 1, Fill = guideBrush, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _crossH = new Rectangle { Height = 1, Fill = guideBrush, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        Canvas.SetTop(_crossV, 0);
        Canvas.SetLeft(_crossH, 0);
        root.Children.Add(_crossV);
        root.Children.Add(_crossH);

        // Selection rectangle: faint inner wash + 1px white border + 1px black
        // outer edge, all hidden until a drag begins.
        _selFill = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(13, 255, 255, 255)),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            SnapsToDevicePixels = true,
        };
        _selBlack = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
            StrokeThickness = 1,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            SnapsToDevicePixels = true,
        };
        _selWhite = new Rectangle
        {
            Stroke = Brushes.White,
            StrokeThickness = 1,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            SnapsToDevicePixels = true,
        };
        root.Children.Add(_selFill);
        root.Children.Add(_selBlack);
        root.Children.Add(_selWhite);

        _cTL = BuildCorner(new Thickness(2, 2, 0, 0), new CornerRadius(3, 0, 0, 0));
        _cTR = BuildCorner(new Thickness(0, 2, 2, 0), new CornerRadius(0, 3, 0, 0));
        _cBL = BuildCorner(new Thickness(2, 0, 0, 2), new CornerRadius(0, 0, 0, 3));
        _cBR = BuildCorner(new Thickness(0, 0, 2, 2), new CornerRadius(0, 0, 3, 0));
        root.Children.Add(_cTL);
        root.Children.Add(_cTR);
        root.Children.Add(_cBL);
        root.Children.Add(_cBR);

        // Dimensions readout: frosted pill (mono, 500), W in fg, × in fg-3, H in fg.
        _labelText = new TextBlock
        {
            FontFamily = Font("MonoFont"),
            FontSize = 12.5,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _labelHost = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x14, 0x15, 0x17)),
            BorderBrush = Res("HairlineStrongBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 6, 10, 6),
            Child = _labelText,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 8, Direction = 270, Opacity = 0.5, Color = Colors.Black },
        };
        root.Children.Add(_labelHost);

        // Auto-fading entry hint + the inline "too small" message.
        _hint = BuildHint();
        root.Children.Add(_hint);
        _tooSmallMsg = BuildTooSmall();
        root.Children.Add(_tooSmallMsg);

        _hintFadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(4200) };
        _hintFadeTimer.Tick += OnHintFadeTick;
        _tooSmallTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1700) };
        _tooSmallTimer.Tick += OnTooSmallTick;

        w.Content = root;
        w.Loaded += OnLoaded;
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SizeGuides();
        ShowCrosshair(true);
        PositionHint();
        ShowHint();
    }

    private void SizeGuides()
    {
        if (_window == null) return;
        if (_crossV != null) _crossV.Height = _window.ActualHeight;
        if (_crossH != null) _crossH.Width = _window.ActualWidth;
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
        // A fresh press always starts a clean selection - clear any lingering
        // hint/too-small/crosshair chrome first so nothing stacks.
        HideTooSmall();
        HideHintNow();
        ShowCrosshair(false);
        _state = SelectionState.Dragging;
        _dragStart = e.GetPosition(_window);
        _currentDip = new Rect(_dragStart, new Size(0, 0));
        _window!.CaptureMouse();
        ShowSelectionVisuals(true);
        _labelHost!.Visibility = Visibility.Visible;
        UpdateVisuals();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_state == SelectionState.Idle)
        {
            // Track the crosshair guide-lines with the cursor.
            var c = e.GetPosition(_window);
            if (_crossV != null) Canvas.SetLeft(_crossV, c.X);
            if (_crossH != null) Canvas.SetTop(_crossH, c.Y);
            return;
        }
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

        // A stray click with no meaningful drag just re-arms, silently.
        if (dip.Width < 3 && dip.Height < 3)
        {
            ResetToIdle();
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
            // Window already gone (race during teardown) - treat as cancel.
            _logger.LogWarning(ex, "ResolveSelection failed; treating as cancel");
            Cancel();
            return;
        }

        if (physWidth < AppSettings.MinRegionWidth || physHeight < AppSettings.MinRegionHeight)
        {
            ShowTooSmall();
            return;
        }

        _state = SelectionState.Confirmed;
        _hintFadeTimer?.Stop();
        HideTooSmall();
        _logger.LogInformation(
            "Selection confirmed: monitor=0x{Mon:X} local=({X},{Y}) physical={PW}x{PH} (dip={DW:F1}x{DH:F1})",
            region.Monitor.ToInt64(), region.X, region.Y, region.Width, region.Height,
            dip.Width, dip.Height);
        _tcs?.TrySetResult(SelectionResult.Confirm(region, dip));
        // NOTE: window is left open. Caller transitions it to RecordingOverlay,
        // which keeps the dim (eased to the locked opacity) so it never flashes off.
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
        var r = _currentDip;

        Place(_selFill!, r.X, r.Y, r.Width, r.Height);
        Place(_selWhite!, r.X, r.Y, r.Width, r.Height);
        Place(_selBlack!, r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2);

        PlaceCorner(_cTL!, r.X - 1, r.Y - 1);
        PlaceCorner(_cTR!, r.Right + 1 - 12, r.Y - 1);
        PlaceCorner(_cBL!, r.X - 1, r.Bottom + 1 - 12);
        PlaceCorner(_cBR!, r.Right + 1 - 12, r.Bottom + 1 - 12);

        if (r.Width >= 1 && r.Height >= 1)
            UpdateDimensionsLabel();
    }

    private void UpdateDimensionsLabel()
    {
        // PointToScreen maps DIPs → device pixels, so this readout is DPI-accurate
        // on mixed-scaling multi-monitor setups (reports real captured pixels).
        var pTL = _window!.PointToScreen(_currentDip.TopLeft);
        var pBR = _window!.PointToScreen(_currentDip.BottomRight);
        int physW = (int)Math.Round(Math.Abs(pBR.X - pTL.X));
        int physH = (int)Math.Round(Math.Abs(pBR.Y - pTL.Y));

        _labelText!.Inlines.Clear();
        _labelText.Inlines.Add(new Run(physW.ToString()) { Foreground = Res("FgBrush") });
        _labelText.Inlines.Add(new Run("  ×  ") { Foreground = Res("Fg3Brush"), FontWeight = FontWeights.Normal });
        _labelText.Inlines.Add(new Run(physH.ToString()) { Foreground = Res("FgBrush") });

        // Position: just outside the selection's bottom-right corner (where the
        // cursor is), right-edge aligned to the region; flips above near the edge.
        _labelHost!.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var labelSz = _labelHost.DesiredSize;
        var (lx, ly) = SelectorMath.PositionLabel(
            _currentDip, labelSz,
            virtualWidth: _window.ActualWidth,
            virtualHeight: _window.ActualHeight);
        Canvas.SetLeft(_labelHost, lx);
        Canvas.SetTop(_labelHost, ly);
    }

    // ----- hint + too-small + crosshair chrome -----

    private void ShowSelectionVisuals(bool show)
    {
        var v = show ? Visibility.Visible : Visibility.Collapsed;
        if (_selFill != null) _selFill.Visibility = v;
        if (_selBlack != null) _selBlack.Visibility = v;
        if (_selWhite != null) _selWhite.Visibility = v;
        if (_cTL != null) _cTL.Visibility = v;
        if (_cTR != null) _cTR.Visibility = v;
        if (_cBL != null) _cBL.Visibility = v;
        if (_cBR != null) _cBR.Visibility = v;
    }

    private void ShowCrosshair(bool show)
    {
        var v = show ? Visibility.Visible : Visibility.Collapsed;
        if (_crossV != null) _crossV.Visibility = v;
        if (_crossH != null) _crossH.Visibility = v;
    }

    private void ShowHint()
    {
        if (_hint == null) return;
        _hint.BeginAnimation(UIElement.OpacityProperty, null);
        _hint.Opacity = 1;
        _hint.Visibility = Visibility.Visible;
        PositionHint();
        _hintFadeTimer?.Stop();
        _hintFadeTimer?.Start();
    }

    private void HideHintNow()
    {
        _hintFadeTimer?.Stop();
        if (_hint == null) return;
        _hint.BeginAnimation(UIElement.OpacityProperty, null);
        _hint.Visibility = Visibility.Collapsed;
    }

    private void OnHintFadeTick(object? sender, EventArgs e)
    {
        _hintFadeTimer?.Stop();
        if (_hint == null || _state != SelectionState.Idle) return;
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        _hint.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void ShowTooSmall()
    {
        // Re-arm for another drag without leaving the overlay; the message clears
        // itself after a moment (or the next mousedown clears it immediately).
        _state = SelectionState.Idle;
        _currentDip = Rect.Empty;
        if (_innerGeo != null) _innerGeo.Rect = new Rect(0, 0, 0, 0);
        ShowSelectionVisuals(false);
        if (_labelHost != null) _labelHost.Visibility = Visibility.Collapsed;
        HideHintNow();
        ShowCrosshair(false);

        if (_tooSmallMsg != null && _window != null)
        {
            // Same component, same spot as the hint - it cross-fades in its place.
            _tooSmallMsg.Visibility = Visibility.Visible;
            PositionTopCenter(_tooSmallMsg);
            _tooSmallMsg.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
        }

        _logger.LogInformation("Selection below minimum {MinW}x{MinH}; showing too-small hint",
            AppSettings.MinRegionWidth, AppSettings.MinRegionHeight);

        _tooSmallTimer?.Stop();
        _tooSmallTimer?.Start();
    }

    private void HideTooSmall()
    {
        _tooSmallTimer?.Stop();
        if (_tooSmallMsg == null) return;
        _tooSmallMsg.BeginAnimation(UIElement.OpacityProperty, null);
        _tooSmallMsg.Visibility = Visibility.Collapsed;
    }

    private void OnTooSmallTick(object? sender, EventArgs e)
    {
        _tooSmallTimer?.Stop();
        if (_state == SelectionState.Idle && _tooSmallMsg is { Visibility: Visibility.Visible })
            ResetToIdle();
    }

    /// <summary>Returns the overlay to a fresh idle selection (clears any drag, message, and re-shows the hint + crosshair).</summary>
    private void ResetToIdle()
    {
        _state = SelectionState.Idle;
        _currentDip = Rect.Empty;
        if (_innerGeo != null) _innerGeo.Rect = new Rect(0, 0, 0, 0);
        ShowSelectionVisuals(false);
        if (_labelHost != null) _labelHost.Visibility = Visibility.Collapsed;
        HideTooSmall();
        SizeGuides();
        ShowCrosshair(true);
        ShowHint();
    }

    /// <summary>
    /// A hotkey re-press while selecting starts over rather than stacking a second
    /// overlay. No-op once the selection is confirmed (recording owns the window)
    /// or canceled. Must be called on the UI thread.
    /// </summary>
    public void RestartSelection()
    {
        if (_window == null) return;
        if (_state is SelectionState.Confirmed or SelectionState.Canceled) return;
        if (_state == SelectionState.Dragging)
        {
            try { _window.ReleaseMouseCapture(); } catch { /* ignore */ }
        }
        _logger.LogInformation("Region selection restarted (hotkey re-press)");
        ResetToIdle();
    }

    private void PositionHint()
    {
        if (_hint != null) PositionTopCenter(_hint);
    }

    /// <summary>
    /// Centers a guidance pill horizontally on the cursor's monitor, 36px from its
    /// top. The hint and the too-small notice are the same component in the same
    /// place (the notice cross-fades in the hint's spot).
    /// </summary>
    private void PositionTopCenter(FrameworkElement el)
    {
        if (_window == null) return;
        el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var sz = el.DesiredSize;
        var mon = CursorMonitorRectDip();
        Canvas.SetLeft(el, mon.X + Math.Max(0, (mon.Width - sz.Width) / 2));
        Canvas.SetTop(el, mon.Y + 36);
    }

    /// <summary>
    /// Bounds of the monitor under the cursor, in window-local DIP coordinates.
    /// The overlay spans the whole virtual desktop, so centering chrome (the hint,
    /// the too-small message) on the virtual rect would drop it at the seam between
    /// monitors. Centering on the cursor's monitor keeps it on the screen in use.
    /// </summary>
    private Rect CursorMonitorRectDip()
    {
        var full = new Rect(0, 0, _window?.ActualWidth ?? 0, _window?.ActualHeight ?? 0);
        if (_window == null) return full;
        try
        {
            if (!GetCursorPos(out var cp)) return full;
            var hMon = MonitorFromPoint(cp, MONITOR_DEFAULTTONEAREST);
            if (hMon == IntPtr.Zero) return full;
            var mi = MONITORINFO.New();
            if (!GetMonitorInfoW(hMon, ref mi)) return full;
            var rc = mi.rcMonitor;
            var tl = _window.PointFromScreen(new Point(rc.Left, rc.Top));
            var br = _window.PointFromScreen(new Point(rc.Right, rc.Bottom));
            return new Rect(tl, br);
        }
        catch
        {
            return full;
        }
    }

    private void ResolveCanceledOnClose()
    {
        if (_tcs is { Task.IsCompleted: false })
        {
            _state = SelectionState.Canceled;
            _tcs.TrySetResult(SelectionResult.Canceled);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hintFadeTimer?.Stop();
        _tooSmallTimer?.Stop();
        ResolveCanceledOnClose();
    }

    private void Cancel()
    {
        if (_state is SelectionState.Confirmed or SelectionState.Canceled) return;
        _state = SelectionState.Canceled;
        _hintFadeTimer?.Stop();
        _tooSmallTimer?.Stop();
        _logger.LogInformation("Region selection canceled by user");
        _tcs?.TrySetResult(SelectionResult.Canceled);
        try { _window?.Close(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hintFadeTimer?.Stop();
        _tooSmallTimer?.Stop();
        if (_state == SelectionState.Idle || _state == SelectionState.Dragging)
        {
            try { _window?.Close(); } catch { }
        }
        _window = null;
    }

    // ----- builders / helpers (code-built to match the existing all-code overlay) -----

    private static void Place(FrameworkElement el, double x, double y, double w, double h)
    {
        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, y);
        el.Width = Math.Max(0, w);
        el.Height = Math.Max(0, h);
    }

    private static void PlaceCorner(Border c, double x, double y)
    {
        Canvas.SetLeft(c, x);
        Canvas.SetTop(c, y);
    }

    private static Border BuildCorner(Thickness thickness, CornerRadius radius) => new()
    {
        Width = 12,
        Height = 12,
        BorderBrush = Brushes.White,
        BorderThickness = thickness,
        CornerRadius = radius,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
        Effect = new DropShadowEffect { BlurRadius = 1, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.55 },
    };

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
    private static FontFamily Font(string key) => (FontFamily)Application.Current.FindResource(key);

    // The hint and the too-small notice are the same component: icon · lead · sep
    // · end, in a fixed-height frosted top-center pill. The hint's end is an Esc
    // key-cap + "to cancel"; the notice's is plain trailing text with a warn icon.
    private Border BuildHint() =>
        BuildGuidancePill(BuildSelectGlyph(Res("Fg2Brush")), "Drag to select a region", BuildHintEnd());

    private Border BuildTooSmall() =>
        BuildGuidancePill(BuildWarnGlyph(Res("WarnBrush")), "Region too small", BuildEndText("Drag a larger box"));

    private Border BuildGuidancePill(FrameworkElement icon, string lead, FrameworkElement end)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        icon.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(icon);

        panel.Children.Add(new TextBlock
        {
            Text = lead,
            Foreground = Res("FgBrush"),
            FontFamily = Font("SansFont"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(11, 0, 0, 0),
        });

        panel.Children.Add(new Border
        {
            Width = 1,
            Height = 15,
            Background = Res("HairlineBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(11, 0, 0, 0),
        });

        end.VerticalAlignment = VerticalAlignment.Center;
        end.Margin = new Thickness(11, 0, 0, 0);
        panel.Children.Add(end);

        return new Border
        {
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x14, 0x15, 0x17)),  // rgba(20,21,23,0.92)
            BorderBrush = Res("HairlineStrongBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 0, 18, 0),
            Child = panel,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 8, Direction = 270, Opacity = 0.5, Color = Colors.Black },
        };
    }

    private FrameworkElement BuildHintEnd()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(BuildKbd("Esc"));
        sp.Children.Add(new TextBlock
        {
            Text = "to cancel",
            Foreground = Res("Fg3Brush"),
            FontFamily = Font("SansFont"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0),
        });
        return sp;
    }

    private FrameworkElement BuildEndText(string text) => new TextBlock
    {
        Text = text,
        Foreground = Res("Fg3Brush"),
        FontFamily = Font("SansFont"),
        FontSize = 13,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Border BuildKbd(string text) => new()
    {
        Background = Res("SurfaceBrush"),
        BorderBrush = Res("HairlineBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(5),
        Padding = new Thickness(7, 2, 7, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text,
            FontFamily = Font("MonoFont"),
            FontSize = 11,
            Foreground = Res("Fg2Brush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    /// <summary>The crop-frame "select a region" glyph (corner brackets + inner square).</summary>
    private static FrameworkElement BuildSelectGlyph(Brush stroke)
    {
        var grid = new Grid { Width = 16, Height = 16 };
        grid.Children.Add(new Path
        {
            Data = Geometry.Parse("M2.5,5.5 V3.5 H4.5 M11.5,3.5 H13.5 V5.5 M13.5,10.5 V12.5 H11.5 M4.5,12.5 H2.5 V10.5"),
            Stroke = stroke,
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        });
        grid.Children.Add(new Rectangle
        {
            Width = 4,
            Height = 4,
            RadiusX = 1,
            RadiusY = 1,
            Stroke = stroke,
            StrokeThickness = 1.2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return grid;
    }

    /// <summary>The too-small glyph: a rounded square with a short centered bar.</summary>
    private static FrameworkElement BuildWarnGlyph(Brush stroke)
    {
        var grid = new Grid { Width = 16, Height = 16 };
        grid.Children.Add(new Border
        {
            Width = 11,
            Height = 11,
            CornerRadius = new CornerRadius(2.5),
            BorderBrush = stroke,
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        grid.Children.Add(new Rectangle
        {
            Width = 4,
            Height = 1.5,
            Fill = stroke,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return grid;
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
    /// Positions the dimensions readout just outside the selection's bottom-right
    /// corner (where the cursor is), with its right edge aligned to the region's
    /// right edge and sitting just below the bottom edge - flipping above when
    /// there's no room - and clamped to stay on the virtual screen.
    /// </summary>
    public static (double X, double Y) PositionLabel(Rect selection, Size labelSize, double virtualWidth, double virtualHeight)
    {
        const double pad = 8, off = 10;
        double right = selection.Right, bottom = selection.Bottom;

        double y = bottom + off;                                   // just below the corner
        if (y + labelSize.Height > virtualHeight - pad)
            y = bottom - labelSize.Height - off;                   // no room below → above, inside
        if (y < pad) y = pad;

        // Align the readout's right edge to the region's right edge; keep on-screen.
        double rightGap = virtualWidth - right;
        rightGap = Math.Max(pad, Math.Min(rightGap, virtualWidth - labelSize.Width - pad));
        double x = virtualWidth - rightGap - labelSize.Width;

        return (x, y);
    }
}
