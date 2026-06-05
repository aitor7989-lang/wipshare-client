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
/// Full-virtual-screen overlay that lets the user drag out a rectangle. The
/// window instance survives selection — on Confirmed, ownership is handed to the
/// caller (via <see cref="OverlayWindow"/>) so <see cref="RecordingOverlay"/> can
/// take it over without a visible flash. On Canceled the window closes.
///
/// Polished selection UX (design app/region-overlay.html): an auto-fading entry
/// hint, a calm inline "too small" message that re-arms for another try instead
/// of bailing out, a DPI-accurate pixel readout, and a fresh-selection-on-rehotkey
/// reset (<see cref="RestartSelection"/>) so a second hotkey press never stacks.
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
        PositionHint();
        ShowHint();
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
        // A fresh press always starts a clean selection — clear any lingering
        // hint/too-small chrome first so nothing stacks.
        HideTooSmall();
        HideHintNow();
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

        // A stray click with no meaningful drag just re-arms, silently — no need
        // to scold the user for a single click.
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
            // Window already gone (race during teardown) — treat as cancel.
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
        // PointToScreen already maps DIPs → device pixels, so this readout is
        // DPI-accurate on mixed-scaling multi-monitor setups.
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

    // ----- hint + too-small chrome -----

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
        if (_border != null) { _border.Visibility = Visibility.Collapsed; _border.Width = 0; _border.Height = 0; }
        if (_labelHost != null) _labelHost.Visibility = Visibility.Collapsed;
        HideHintNow();

        if (_tooSmallMsg != null && _window != null)
        {
            _tooSmallMsg.Visibility = Visibility.Visible;
            _tooSmallMsg.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var sz = _tooSmallMsg.DesiredSize;
            Canvas.SetLeft(_tooSmallMsg, Math.Max(0, (_window.ActualWidth - sz.Width) / 2));
            Canvas.SetTop(_tooSmallMsg, Math.Max(0, (_window.ActualHeight - sz.Height) / 2));
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

    /// <summary>Returns the overlay to a fresh idle selection (clears any drag, message, and re-shows the hint).</summary>
    private void ResetToIdle()
    {
        _state = SelectionState.Idle;
        _currentDip = Rect.Empty;
        if (_innerGeo != null) _innerGeo.Rect = new Rect(0, 0, 0, 0);
        if (_border != null) { _border.Visibility = Visibility.Collapsed; _border.Width = 0; _border.Height = 0; }
        if (_labelHost != null) _labelHost.Visibility = Visibility.Collapsed;
        HideTooSmall();
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
        if (_window == null || _hint == null) return;
        _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var sz = _hint.DesiredSize;
        Canvas.SetLeft(_hint, Math.Max(0, (_window.ActualWidth - sz.Width) / 2));
        Canvas.SetTop(_hint, 38);
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

    // ----- chrome builders (code-built to match the existing all-code overlay) -----

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
    private static FontFamily Font(string key) => (FontFamily)Application.Current.FindResource(key);

    private Border BuildHint()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var glyph = BuildSelectGlyph();
        glyph.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(glyph);

        panel.Children.Add(new TextBlock
        {
            Text = "Drag to select a region",
            Foreground = Res("Fg2Brush"),
            FontFamily = Font("SansFont"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(11, 0, 0, 0),
        });

        panel.Children.Add(new Border
        {
            Width = 1,
            Height = 16,
            Background = Res("HairlineBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0),
        });

        var esc = BuildKeyCap("Esc");
        esc.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(esc);

        panel.Children.Add(new TextBlock
        {
            Text = "to cancel",
            Foreground = Res("Fg2Brush"),
            FontFamily = Font("SansFont"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        });

        return new Border
        {
            Height = 42,
            CornerRadius = new CornerRadius(21),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x14, 0x15, 0x17)),
            BorderBrush = Res("HairlineStrongBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 0, 10, 0),
            Child = panel,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 8, Direction = 270, Opacity = 0.5, Color = Colors.Black },
        };
    }

    private Border BuildTooSmall()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var glyph = BuildTooSmallGlyph();
        glyph.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(glyph);

        var text = new TextBlock
        {
            FontFamily = Font("SansFont"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 0, 0),
        };
        text.Inlines.Add(new Run("That region is too small. ") { Foreground = Res("FgBrush") });
        text.Inlines.Add(new Run("Drag a larger box.") { Foreground = Res("Fg3Brush") });
        panel.Children.Add(text);

        return new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x14, 0x15, 0x17)),
            BorderBrush = Res("HairlineStrongBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(15, 11, 15, 11),
            Child = panel,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Direction = 270, Opacity = 0.55, Color = Colors.Black },
        };
    }

    private static Border BuildKeyCap(string text) => new()
    {
        Background = Res("SurfaceBrush"),
        BorderBrush = Res("HairlineBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(5),
        Padding = new Thickness(8, 3, 8, 3),
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
    private static FrameworkElement BuildSelectGlyph()
    {
        var grid = new Grid { Width = 16, Height = 16 };
        grid.Children.Add(new Path
        {
            Data = Geometry.Parse("M2.5,5.5 V3.5 H4.5 M11.5,3.5 H13.5 V5.5 M13.5,10.5 V12.5 H11.5 M4.5,12.5 H2.5 V10.5"),
            Stroke = Res("Fg2Brush"),
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
            Stroke = Res("Fg2Brush"),
            StrokeThickness = 1.2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return grid;
    }

    /// <summary>The "too small" glyph: a rounded square with a short bar.</summary>
    private static FrameworkElement BuildTooSmallGlyph()
    {
        var grid = new Grid { Width = 16, Height = 16 };
        grid.Children.Add(new Border
        {
            Width = 11,
            Height = 11,
            CornerRadius = new CornerRadius(3),
            BorderBrush = Res("Fg3Brush"),
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        grid.Children.Add(new Rectangle
        {
            Width = 5,
            Height = 1.5,
            Fill = Res("Fg3Brush"),
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
