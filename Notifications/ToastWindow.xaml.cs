using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Extensions.Logging;
using static WipShare.Client.Native.Win32Interop;

namespace WipShare.Client.Notifications;

/// <summary>
/// One desktop toast: a borderless, no-activate, capture-excluded window that
/// renders any <see cref="ToastKind"/> and transitions in place (uploading →
/// success/failed). Placement and lifecycle are owned by <see cref="ToastHost"/>.
/// </summary>
public partial class ToastWindow : Window
{
    private const string IconUpload = "M8,13 V4 M4.5,7.5 L8,4 L11.5,7.5";
    private const string IconAlert  = "M8,2.5 L14.5,13 H1.5 Z M8,6.5 V9.5 M8,11.7 L8,12.1";
    private const string IconSave   = "M3,4.5 A1.5,1.5 0 0 1 4.5,3 H9.5 L13,6.5 V11.5 A1.5,1.5 0 0 1 11.5,13 H4.5 A1.5,1.5 0 0 1 3,11.5 Z M5.5,3 V6 H9 M5.5,9.5 H10.5";

    private readonly ILogger _logger;
    private bool _leaving;
    private AnimationClock? _countdownClock;

    /// <summary>Raised when the toast should be removed from the stack (close, auto-dismiss).</summary>
    public event Action<ToastWindow>? Dismissed;
    public event Action<ToastWindow>? OpenRequested;
    public event Action<ToastWindow>? CopyAgainRequested;
    public event Action<ToastWindow>? RetryRequested;
    public event Action<ToastWindow>? SetupRequested;

    public ToastViewModel Model { get; }

    public ToastWindow(ToastViewModel model, ILoggerFactory loggerFactory)
    {
        Model = model;
        _logger = loggerFactory.CreateLogger<ToastWindow>();
        InitializeComponent();

        RootCard.Opacity = 0;            // enter animation fades/slides it in
        EnterTransform.X = 34;
        RootCard.MouseLeftButtonUp += OnBodyClick;
        RootCard.MouseEnter += (_, _) => _countdownClock?.Controller?.Pause();
        RootCard.MouseLeave += (_, _) => { if (!_leaving) _countdownClock?.Controller?.Resume(); };

        Loaded += OnLoaded;
        ApplyState();
    }

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero) return;
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        if (!SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE))
            _logger.LogWarning("Toast WDA_EXCLUDEFROMCAPTURE failed: {Err}", Marshal.GetLastWin32Error());
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = new Duration(TimeSpan.FromMilliseconds(280));
        RootCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220))));
        EnterTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(34, 0, dur) { EasingFunction = ease });
    }

    // ----- state rendering -----

    private void ApplyState()
    {
        var fg2 = (Brush)FindResource("Fg2Brush");
        var surface2 = (Brush)FindResource("Surface2Brush");
        var err = (Brush)FindResource("ErrBrush");

        // reset toggles
        IconWrap.Visibility = Visibility.Visible;
        ThumbWrap.Visibility = Visibility.Collapsed;
        DescText.Visibility = Visibility.Visible;
        DescMono.Visibility = Visibility.Collapsed;
        ProgressTrack.Visibility = Visibility.Collapsed;
        Actions.Visibility = Visibility.Collapsed;
        TitleCheck.Visibility = Visibility.Collapsed;
        Countdown.Visibility = Visibility.Collapsed;
        BtnOpen.Visibility = BtnCopyAgain.Visibility = BtnRetry.Visibility = BtnSetup.Visibility = Visibility.Collapsed;
        RootCard.Cursor = Cursors.Arrow;

        switch (Model.Kind)
        {
            case ToastKind.Uploading:
                IconWrap.Background = surface2;
                SetIcon(IconUpload, fg2, 1.6);
                TitleText.Text = $"Uploading {Model.FileName}";
                ProgressTrack.Visibility = Visibility.Visible;
                ApplyProgress();
                break;

            case ToastKind.Success:
                IconWrap.Visibility = Visibility.Collapsed;
                ThumbWrap.Visibility = Visibility.Visible;
                LoadThumbnail();
                TitleText.Text = "Link copied";
                TitleCheck.Visibility = Visibility.Visible;
                DescText.Visibility = Visibility.Collapsed;
                DescMono.Visibility = Visibility.Visible;
                DescMono.Text = StripScheme(Model.ViewerUrl ?? "");
                Actions.Visibility = Visibility.Visible;
                BtnOpen.Visibility = Visibility.Visible;
                BtnCopyAgain.Visibility = Visibility.Visible;
                RootCard.Cursor = Cursors.Hand;
                Countdown.Visibility = Visibility.Visible;
                break;

            case ToastKind.Failed:
                IconWrap.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xE3, 0x5D, 0x6A));
                SetIcon(IconAlert, err, 1.8);
                TitleText.Text = "Upload failed";
                DescText.Text = "Saved locally — your clip is safe. We’ll keep retrying.";
                Actions.Visibility = Visibility.Visible;
                BtnRetry.Visibility = Visibility.Visible;
                break;

            case ToastKind.LocalOnly:
                IconWrap.Background = surface2;   // neutral, NOT error styling
                SetIcon(IconSave, fg2, 1.5);
                TitleText.Text = "Saved to your computer";
                DescText.Text = "Add an invite code to share a link.";
                Actions.Visibility = Visibility.Visible;
                BtnSetup.Visibility = Visibility.Visible;
                Countdown.Visibility = Visibility.Visible;
                break;
        }
    }

    private void SetIcon(string data, Brush stroke, double thickness)
    {
        IconPath.Data = Geometry.Parse(data);
        IconPath.Stroke = stroke;
        IconPath.StrokeThickness = thickness;
    }

    private void ApplyProgress()
    {
        double frac = Math.Clamp(Model.ProgressFraction, 0, 1);
        ProgressTrack.UpdateLayout();
        double track = ProgressTrack.ActualWidth;
        if (track <= 0) track = 364 - 26 - 30 - 11; // body width estimate pre-layout
        ProgressBar.Width = frac * track;
        DescText.Text = $"{Model.SentMb:0.0} / {Model.TotalMb:0.0} MB · {Model.ProgressNote}";
    }

    private void LoadThumbnail()
    {
        ThumbPlay.Visibility = Visibility.Visible;
        ThumbImage.Source = null;
        var path = Model.ThumbnailPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;        // don't lock the file
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            using (var fs = File.OpenRead(path)) { bmp.StreamSource = fs; bmp.EndInit(); }
            bmp.Freeze();
            ThumbImage.Source = bmp;
            ThumbPlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Toast thumbnail load failed for {Path}", path);
        }
    }

    private static string StripScheme(string url) => url.Replace("https://", "").Replace("http://", "");

    // ----- public transitions (called by the host on the UI thread) -----

    /// <summary>Updates the uploading bar + MB/seconds-left line.</summary>
    public void UpdateProgress(double sentMb, double totalMb, double fraction, string note)
    {
        Model.SentMb = sentMb;
        Model.TotalMb = totalMb;
        Model.ProgressFraction = fraction;
        Model.ProgressNote = note;
        if (Model.Kind == ToastKind.Uploading) ApplyProgress();
    }

    public void TransitionToSuccess(string viewerUrl, string? thumbnailPath)
    {
        Model.Kind = ToastKind.Success;
        Model.ViewerUrl = viewerUrl;
        Model.ThumbnailPath = thumbnailPath;
        ApplyState();
        ArmAutoDismiss(TimeSpan.FromSeconds(7));
    }

    public void TransitionToFailed()
    {
        Model.Kind = ToastKind.Failed;
        ApplyState();
    }

    /// <summary>Morphs a failed toast back into the uploading state (Retry now).</summary>
    public void TransitionToUploading(string note)
    {
        Model.Kind = ToastKind.Uploading;
        Model.ProgressFraction = 0;
        Model.SentMb = 0;
        Model.ProgressNote = note;
        StopAutoDismiss();
        ApplyState();
    }

    public void ShowAsLocalOnly() => ArmAutoDismiss(TimeSpan.FromSeconds(7));

    // ----- auto-dismiss countdown (hover pauses) -----

    private void ArmAutoDismiss(TimeSpan duration)
    {
        StopAutoDismiss();
        Countdown.Visibility = Visibility.Visible;
        var anim = new DoubleAnimation(1, 0, new Duration(duration)) { FillBehavior = FillBehavior.Stop };
        var clock = anim.CreateClock();
        clock.Completed += (_, _) => { if (!_leaving) StartLeave(); };
        _countdownClock = clock;
        CountdownScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, clock);
    }

    private void StopAutoDismiss()
    {
        if (_countdownClock != null)
        {
            CountdownScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, null);
            _countdownClock = null;
        }
    }

    // ----- leave -----

    /// <summary>Animates the toast out, then closes it. Raises Dismissed up-front so the host reflows the gap.</summary>
    public void StartLeave()
    {
        if (_leaving) return;
        _leaving = true;
        StopAutoDismiss();
        Dismissed?.Invoke(this);   // host removes from list + reflows others up into the gap

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var slide = new DoubleAnimation(0, 40, new Duration(TimeSpan.FromMilliseconds(200))) { EasingFunction = ease };
        var fade = new DoubleAnimation(RootCard.Opacity, 0, new Duration(TimeSpan.FromMilliseconds(180)));
        fade.Completed += (_, _) => { try { Close(); } catch { /* already closing */ } };
        EnterTransform.BeginAnimation(TranslateTransform.XProperty, slide);
        RootCard.BeginAnimation(OpacityProperty, fade);
    }

    // ----- input -----

    private void OnCloseClick(object sender, RoutedEventArgs e) => StartLeave();
    private void OnOpenClick(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(this);
    private void OnRetryClick(object sender, RoutedEventArgs e) => RetryRequested?.Invoke(this);
    private void OnSetupClick(object sender, RoutedEventArgs e) => SetupRequested?.Invoke(this);

    private void OnCopyAgainClick(object sender, RoutedEventArgs e)
    {
        CopyAgainRequested?.Invoke(this);
        FlashCopied();
    }

    private void OnBodyClick(object sender, MouseButtonEventArgs e)
    {
        if (Model.Kind == ToastKind.Success) OpenRequested?.Invoke(this);
    }

    /// <summary>Briefly flips "Copy again" → "Copied".</summary>
    private void FlashCopied()
    {
        CopyAgainText.Text = "Copied";
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
        t.Tick += (_, _) => { t.Stop(); CopyAgainText.Text = "Copy again"; };
        t.Start();
    }
}
