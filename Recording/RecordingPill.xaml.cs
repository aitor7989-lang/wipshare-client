using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.Logging;
using WipShare.Client.Capture;
using static WipShare.Client.Native.Win32Interop;

namespace WipShare.Client.Recording;

/// <summary>
/// The floating recording control bar. It owns BOTH the pre-record countdown
/// (3-2-1 in the timer slot, per the design) and the recording state (M:SS/M:SS
/// timer, inset progress line, Cancel + Finish + drag). It floats centered just
/// below the captured region every time (no remembered position), can be dragged
/// during a session, never steals focus (WS_EX_NOACTIVATE), and is excluded from
/// capture so it never films itself. Pause/restart/mic are intentionally absent.
/// </summary>
public partial class RecordingPill : Window
{
    private readonly ILogger _logger;
    private readonly SelectedRegion _region;
    private readonly TimeSpan _total;

    private bool _dragging;
    private POINT _dragCursorStart;
    private int _winStartX, _winStartY;

    /// <summary>Cancel — discard the recording (red X, or Esc via the app hook).</summary>
    public event Action? CancelRequested;
    /// <summary>Finish — stop early but keep + upload the clip (indigo check).</summary>
    public event Action? FinishRequested;

    public RecordingPill(SelectedRegion region, TimeSpan total, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<RecordingPill>();
        _region = region;
        _total = total;

        InitializeComponent();

        TotRun.Text = Format(total);
        CurRun.Text = Format(TimeSpan.Zero);

        // Start in the countdown state: number shown, timer + progress hidden,
        // Finish dimmed (only Cancel + drag are live until "1" clears).
        TimerBlock.Visibility = Visibility.Collapsed;
        CountBlock.Visibility = Visibility.Visible;
        ProgressFill.Visibility = Visibility.Collapsed;
        FinishButton.IsEnabled = false;
        FinishButton.Opacity = 0.35;

        Loaded += OnLoaded;
    }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Runs the 3 → 2 → 1 countdown in the pill's slot. True = finished, false = canceled.</summary>
    public async Task<bool> RunCountdownAsync(CancellationToken ct)
    {
        for (int n = 3; n >= 1; n--)
        {
            SetCountNumber(n);
            try { await Task.Delay(1000, ct); }
            catch (OperationCanceledException) { return false; }
        }
        return true;
    }

    private void SetCountNumber(int n)
    {
        CountBlock.Text = n.ToString();
        // Calm pulse: 0.55 → 1 → 0.55 over the 1s the number is shown (cdpulse).
        var pulse = new DoubleAnimation(0.55, 1.0, new Duration(TimeSpan.FromMilliseconds(500)))
        {
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        CountBlock.BeginAnimation(OpacityProperty, pulse);
    }

    /// <summary>Switches from countdown to the live recording state and re-centers below the region.</summary>
    public void SwitchToRecording()
    {
        CountBlock.BeginAnimation(OpacityProperty, null);
        CountBlock.Visibility = Visibility.Collapsed;
        TimerBlock.Visibility = Visibility.Visible;
        ProgressFill.Visibility = Visibility.Visible;
        FinishButton.IsEnabled = true;
        FinishButton.Opacity = 1.0;

        // The timer is wider than a single digit, so the pill grew — re-place so
        // the recording bar stays centered on the region's bottom edge.
        Dispatcher.BeginInvoke(new Action(PlacePill), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Updates the timer text + progress line. Call on the UI thread.</summary>
    public void SetElapsed(TimeSpan elapsed)
    {
        if (elapsed > _total) elapsed = _total;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

        CurRun.Text = Format(elapsed);

        double frac = _total.TotalMilliseconds <= 0
            ? 0
            : Math.Clamp(elapsed.TotalMilliseconds / _total.TotalMilliseconds, 0, 1);
        // Full-width fill along the bottom edge; the rounded clip rounds the ends.
        ProgressFill.Width = frac * PillGrid.ActualWidth;
    }

    /// <summary>
    /// Clips the pill's content to its rounded inner rectangle so the flush
    /// bottom progress line follows the pill's bottom corners. A ROUNDED clip
    /// (not the rectangular ClipToBounds, which drew a faint box) on the
    /// non-shadowed content grid — the shadow stays on the outer border.
    /// </summary>
    private void OnPillGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PillGrid.Clip = new RectangleGeometry(
            new Rect(0, 0, PillGrid.ActualWidth, PillGrid.ActualHeight), 11, 11);
    }

    private static string Format(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    // ----- chrome / placement -----

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero) return;

        // Never activate (don't steal focus from the recorded app) + hide from
        // alt-tab + exclude from capture.
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));

        if (!SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE))
            _logger.LogWarning("Pill WDA_EXCLUDEFROMCAPTURE failed: {Err}", Marshal.GetLastWin32Error());
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try { PlacePill(); }
        catch (Exception ex) { _logger.LogError(ex, "Pill placement failed"); }
        finally { Opacity = 1; }
    }

    /// <summary>
    /// Always places the pill horizontally centered on the drawn region, just
    /// below its bottom edge (the 16px transparent window margin supplies the
    /// gap). No remembered position — the region drawn this time is the only
    /// context. Flips above / clamps to the work area when there's no room below.
    /// </summary>
    private void PlacePill()
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT win)) return;
        int pw = win.Width, ph = win.Height;

        var mi = MONITORINFO.New();
        if (!GetMonitorInfoW(_region.Monitor, ref mi)) return;

        int regL = mi.rcMonitor.Left + _region.X;
        int regT = mi.rcMonitor.Top + _region.Y;
        int x = regL + (_region.Width - pw) / 2;
        int y = regT + _region.Height;                 // window top at region bottom; margin = the gap
        if (y + ph > mi.rcWork.Bottom) y = regT - ph;  // no room below → flip above
        x = Clamp(x, mi.rcWork.Left, Math.Max(mi.rcWork.Left, mi.rcWork.Right - pw));
        y = Clamp(y, mi.rcWork.Top, Math.Max(mi.rcWork.Top, mi.rcWork.Bottom - ph));

        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, (uint)(SWP_NOSIZE | SWP_NOACTIVATE));
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    // ----- drag (physical pixels — DPI-robust; in-session only, not persisted) -----

    private void OnPillMouseDown(object sender, MouseButtonEventArgs e)
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero) return;
        if (!GetCursorPos(out _dragCursorStart) || !GetWindowRect(hwnd, out RECT r)) return;
        _winStartX = r.Left;
        _winStartY = r.Top;
        _dragging = true;
        PillBorder.CaptureMouse();
        e.Handled = true;
    }

    private void OnPillMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        if (!GetCursorPos(out POINT cur)) return;
        int nx = _winStartX + (cur.X - _dragCursorStart.X);
        int ny = _winStartY + (cur.Y - _dragCursorStart.Y);
        SetWindowPos(Handle, HWND_TOPMOST, nx, ny, 0, 0, (uint)(SWP_NOSIZE | SWP_NOACTIVATE));
    }

    private void OnPillMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        PillBorder.ReleaseMouseCapture();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
    private void OnFinishClick(object sender, RoutedEventArgs e) => FinishRequested?.Invoke();
}
