using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using WipShare.Client.Native;

namespace WipShare.Client.Notifications;

/// <summary>
/// Owns the bottom-right toast stack: placement (taskbar/work-area + DPI aware),
/// the 3-visible cap with a "Clear N older" overflow pill, and smooth reflow as
/// toasts enter and leave. Each toast is its own no-activate, capture-excluded
/// window. All members must be touched on the UI thread.
/// </summary>
public sealed class ToastHost : IDisposable
{
    private const int MaxVisible = 3;
    private const double MarginRight = 14;   // gap from the work-area right edge (DIP)
    private const double MarginBottom = 12;  // gap from the work-area bottom edge (DIP)
    private const double Gap = 10;           // visible gap between cards (DIP)
    private const double CardMargin = 18;    // transparent shadow margin baked into each window

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ToastHost> _logger;
    private readonly List<ToastWindow> _toasts = new();
    private readonly Dictionary<ToastWindow, double> _targetTop = new();
    private readonly HashSet<ToastWindow> _placed = new();
    private readonly DispatcherTimer _ease;

    private Window? _pill;
    private TextBlock? _pillLabel;
    private bool _disposed;

    public ToastHost(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ToastHost>();
        _ease = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _ease.Tick += OnEaseTick;
    }

    /// <summary>Creates, shows, and stacks a toast. Caller wires the window's action events.</summary>
    public ToastWindow AddToast(ToastViewModel vm)
    {
        var w = new ToastWindow(vm, _loggerFactory);
        w.Dismissed += OnToastDismissed;
        w.SizeChanged += (_, _) => Relayout();
        _toasts.Add(w);
        w.Show();
        Relayout();
        return w;
    }

    private void OnToastDismissed(ToastWindow w)
    {
        // Raised at the START of the leave animation; remove from the stack now so
        // the others reflow up into the gap while it slides out.
        if (_toasts.Remove(w))
        {
            _targetTop.Remove(w);
            _placed.Remove(w);
            Relayout();
        }
    }

    private void Relayout()
    {
        if (_disposed) return;

        var anchor = TaskbarInfo.GetPrimary();
        var work = SystemParameters.WorkArea; // DIPs, primary, excludes a docked taskbar
        double autoHide = anchor.AutoHideClearance > 0 ? 46 : 0;

        double visibleRight = work.Right - MarginRight;
        double visibleBottom = work.Bottom - MarginBottom - autoHide;
        double windowLeft = visibleRight - (400 - CardMargin); // card right = Left + 18 + 364

        int count = _toasts.Count;
        int firstVisible = Math.Max(0, count - MaxVisible);

        double cursorBottom = visibleBottom;
        for (int i = count - 1; i >= 0; i--)
        {
            var w = _toasts[i];
            bool visible = i >= firstVisible;
            if (!visible)
            {
                if (w.IsVisible) w.Hide();
                continue;
            }
            if (!w.IsVisible) w.Show();

            double h = w.ActualHeight > 0 ? w.ActualHeight : 92; // fallback before first measure
            double targetTop = cursorBottom - h + CardMargin;
            w.Left = windowLeft;
            SetTarget(w, targetTop);

            double cardTop = targetTop + CardMargin;
            cursorBottom = cardTop - Gap;
        }

        UpdateOverflowPill(firstVisible, windowLeft, cursorBottom);
    }

    private void SetTarget(ToastWindow w, double top)
    {
        _targetTop[w] = top;
        if (!_placed.Contains(w))
        {
            // First placement: snap (don't fly in from the top of the screen).
            w.Top = top;
            _placed.Add(w);
        }
        else if (!_ease.IsEnabled)
        {
            _ease.Start();
        }
    }

    private void OnEaseTick(object? sender, EventArgs e)
    {
        bool anyMoving = false;
        foreach (var w in _toasts)
        {
            if (!_targetTop.TryGetValue(w, out double target)) continue;
            double cur = w.Top;
            double delta = target - cur;
            if (Math.Abs(delta) < 0.5) { w.Top = target; continue; }
            w.Top = cur + delta * 0.28; // exponential ease toward target
            anyMoving = true;
        }
        if (!anyMoving) _ease.Stop();
    }

    // ----- overflow pill -----

    private void UpdateOverflowPill(int hiddenCount, double windowLeft, double topmostCardTopCursor)
    {
        if (hiddenCount <= 0)
        {
            if (_pill is { IsVisible: true }) _pill.Hide();
            return;
        }

        EnsurePill();
        _pillLabel!.Text = $"Clear {hiddenCount} older";
        _pill!.UpdateLayout();
        // right-aligned, sitting above the topmost visible card (cursor is its target gap line)
        double right = windowLeft + (400 - CardMargin);
        _pill.Left = right - _pill.ActualWidth + CardMargin;
        _pill.Top = topmostCardTopCursor - _pill.ActualHeight + CardMargin;
        if (!_pill.IsVisible) _pill.Show();
    }

    private void EnsurePill()
    {
        if (_pill != null) return;

        var label = new TextBlock
        {
            Foreground = (Brush)Application.Current.FindResource("Fg2Brush"),
            FontFamily = (FontFamily)Application.Current.FindResource("SansFont"),
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "Clear 0 older",
        };
        var x = new Path
        {
            Data = Geometry.Parse("M0,0 L8,8 M8,0 L0,8"),
            Stroke = (Brush)Application.Current.FindResource("Fg3Brush"),
            StrokeThickness = 1.5,
            Width = 9,
            Height = 9,
            Stretch = Stretch.None,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Children = { x, label } };
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD2, 0x16, 0x17, 0x1A)),
            BorderBrush = (Brush)Application.Current.FindResource("HairlineBrush"),
            BorderThickness = new Thickness(1),
            Height = 28,
            CornerRadius = new CornerRadius(14),   // = height/2 → a true capsule (WPF doesn't clamp like CSS)
            Padding = new Thickness(11, 0, 11, 0),
            Margin = new Thickness(CardMargin),
            Child = content,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        border.MouseLeftButtonUp += (_, _) => ClearHiddenBatch();

        _pill = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = border,
            Title = "WipShare",
        };
        _pill.SourceInitialized += (_, _) => ToastInterop.MakeNoActivateCaptureExcluded(_pill!, _logger);
        _pillLabel = label;
        _pill.Show();
    }

    private void ClearHiddenBatch()
    {
        int hidden = Math.Max(0, _toasts.Count - MaxVisible);
        // oldest are at the front of the list
        foreach (var w in _toasts.Take(hidden).ToList())
            w.StartLeave();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ease.Stop();
        foreach (var w in _toasts.ToList())
        {
            try { w.Close(); } catch { /* ignore */ }
        }
        _toasts.Clear();
        try { _pill?.Close(); } catch { /* ignore */ }
        _pill = null;
    }
}
