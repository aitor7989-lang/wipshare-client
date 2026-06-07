using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.Logging;
using WipShare.Client.Config;
using WipShare.Client.Hotkey;

namespace WipShare.Client.Settings;

/// <summary>
/// The Settings window. A normal activatable window (it can hold focus, so the
/// hotkey-rebind row captures keys directly). Every row applies immediately and
/// persists; there is no global Save/Cancel.
/// </summary>
public partial class SettingsWindow : Window
{
    private enum HkMode { Default, Capturing, Candidate }

    // Combos owned by another app / the OS - a friendly pre-filter (the real check
    // is the RegisterHotKey attempt on Set).
    private static readonly Dictionary<string, string> Reserved = new()
    {
        ["Ctrl+Shift+S"] = "Snipping Tool",
        ["Win+Shift+S"] = "Windows screenshot",
        ["Ctrl+Shift+Esc"] = "Task Manager",
        ["Alt+Tab"] = "Windows",
        ["Alt+F4"] = "Windows",
        ["Win+D"] = "Windows",
        ["Win+L"] = "Windows",
    };

    private readonly AccessConfig _accessConfig;
    private readonly HotkeyManager _hotkeyManager;
    private readonly Action _openFirstRun;
    private readonly ILogger _logger;

    private HkMode _mode = HkMode.Default;
    private ModifierKeys _candMods;
    private Key _candKey;

    public SettingsWindow(AccessConfig accessConfig, HotkeyManager hotkeyManager, Action openFirstRun, ILoggerFactory loggerFactory)
    {
        _accessConfig = accessConfig;
        _hotkeyManager = hotkeyManager;
        _openFirstRun = openFirstRun;
        _logger = loggerFactory.CreateLogger<SettingsWindow>();

        InitializeComponent();
        LoadState();

        // Clip content to the chrome's rounded corners (design overflow:hidden) so
        // the close button's red hover never spills past the corner.
        ContentRoot.SizeChanged += (_, _) => UpdateCornerClip();
        UpdateCornerClip();

        PreviewKeyDown += OnWindowPreviewKeyDown;
        PreviewMouseDown += OnWindowPreviewMouseDown;
        Activated += (_, _) => { RenderConnection(); StartupToggle.IsChecked = StartupRegistry.IsEnabled(); };
        Loaded += (_, _) => HookScrollbar();
    }

    private void UpdateCornerClip() =>
        ContentRoot.Clip = new RectangleGeometry(
            new Rect(0, 0, ContentRoot.ActualWidth, ContentRoot.ActualHeight), 7, 7);

    // ----- overlay scrollbar (design ds/scrollbar.html) -----

    private ScrollBar? _vbar;
    private System.Windows.Threading.DispatcherTimer? _barHideTimer;
    private bool _scrolledRecently;

    /// <summary>
    /// Drives the custom overlay scrollbar's fade: visible while the scroll area is
    /// hovered OR for ~1s after a scroll, then it eases back out.
    /// </summary>
    private void HookScrollbar()
    {
        BodyScroll.ApplyTemplate();
        _vbar = BodyScroll.Template.FindName("PART_VerticalScrollBar", BodyScroll) as ScrollBar;
        if (_vbar == null) return;

        _barHideTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _barHideTimer.Tick += (_, _) => { _barHideTimer!.Stop(); _scrolledRecently = false; UpdateBar(); };

        BodyScroll.ScrollChanged += (_, _) =>
        {
            _scrolledRecently = true;
            _barHideTimer!.Stop();
            _barHideTimer.Start();
            UpdateBar();
        };
        BodyScroll.MouseEnter += (_, _) => UpdateBar();
        BodyScroll.MouseLeave += (_, _) => UpdateBar();
        UpdateBar();
    }

    private void UpdateBar()
    {
        if (_vbar == null) return;
        double target = (BodyScroll.IsMouseOver || _scrolledRecently) ? 1.0 : 0.0;
        _vbar.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }

    private void LoadState()
    {
        RenderCombo(ComboView, AppSettings.HotkeyModifiers, AppSettings.HotkeyKey);

        SetCountdownUi(AppSettings.CountdownEnabled);
        AutoCopyToggle.IsChecked = AppSettings.AutoCopyLink;
        OpenLinkToggle.IsChecked = AppSettings.OpenLinkAfterRecording;
        KeepLocalToggle.IsChecked = AppSettings.KeepLocalCopy;
        UpdateLocRowVisibility();
        SetLocPath(AppSettings.OutputFolder);
        StartupToggle.IsChecked = StartupRegistry.IsEnabled();
        RenderConnection();
    }

    // ----- chrome -----

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) { try { DragMove(); } catch { /* not draggable now */ } }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // ----- recording: hotkey rebind -----

    private void OnHkChangeClick(object sender, RoutedEventArgs e) => StartCapture();

    private void StartCapture()
    {
        _mode = HkMode.Capturing;
        HkDefault.Visibility = Visibility.Collapsed;
        HkConfirm.Visibility = Visibility.Collapsed;
        HkListen.Visibility = Visibility.Visible;
        HkListenText.Text = "Press a key combination…";
        ClearFeedback();
    }

    private void CancelCapture()
    {
        _mode = HkMode.Default;
        HkListen.Visibility = Visibility.Collapsed;
        HkConfirm.Visibility = Visibility.Collapsed;
        HkDefault.Visibility = Visibility.Visible;
        ClearFeedback();
    }

    private void OnHkCancelClick(object sender, RoutedEventArgs e) => CancelCapture();

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_mode == HkMode.Candidate && (e.Key == Key.Enter))
        {
            e.Handled = true;
            CommitHotkey();
            return;
        }
        if (_mode != HkMode.Capturing) return;

        e.Handled = true; // swallow keys while capturing so nothing else reacts

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { CancelCapture(); return; }

        var mods = Keyboard.Modifiers;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= ModifierKeys.Windows;

        // still building - a modifier is held but no main key yet
        if (IsModifierKey(key))
        {
            HkListenText.Text = mods == ModifierKeys.None ? "Press a key combination…" : DescribeMods(mods) + " + …";
            return;
        }

        var parts = ComboParts(mods, key);

        if (mods == ModifierKeys.None)
        {
            ShowFeedback(false, $"“{HotkeyManager.KeyName(key)}” needs a modifier like Ctrl or Alt.");
            return;
        }
        if (Reserved.TryGetValue(string.Join("+", parts), out var owner))
        {
            ShowFeedback(false, $"{string.Join(" + ", parts)} is in use by {owner}. Try another.");
            return;
        }

        // valid candidate
        _candMods = mods;
        _candKey = key;
        RenderCombo(CandCombo, mods, key);
        _mode = HkMode.Candidate;
        HkListen.Visibility = Visibility.Collapsed;
        HkConfirm.Visibility = Visibility.Visible;
        ClearFeedback();
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // a click outside the hotkey row cancels capture (matches the design)
        if (_mode == HkMode.Capturing && !IsWithin(e.OriginalSource as DependencyObject, HkRow))
            CancelCapture();
    }

    private void OnHkSetClick(object sender, RoutedEventArgs e) => CommitHotkey();

    private void CommitHotkey()
    {
        if (_mode != HkMode.Candidate) return;

        if (_hotkeyManager.TryReRegister(_candMods, _candKey))
        {
            RenderCombo(ComboView, _candMods, _candKey);
            CancelCapture();                       // back to default chips
            ShowFeedback(true, "Shortcut updated.");
            AutoClearFeedback();
        }
        else
        {
            // The OS rejected it (already owned); the old hotkey stays active.
            ShowFeedback(false, "That combination is in use. Try another.");
            StartCapture();                        // let them pick a different one
        }
    }

    // ----- recording: countdown -----

    private void OnCountdown3Click(object sender, RoutedEventArgs e) => SetCountdown(true);
    private void OnCountdownOffClick(object sender, RoutedEventArgs e) => SetCountdown(false);

    private void SetCountdown(bool enabled)
    {
        AppSettings.CountdownEnabled = enabled;
        SetCountdownUi(enabled);
        CountdownChip.IsChecked = false; // close popup
    }

    private void SetCountdownUi(bool enabled)
    {
        CountdownVal.Text = enabled ? "3 seconds" : "Off";
        Cd3Check.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        CdOffCheck.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
    }

    // ----- sharing -----

    private void OnAutoCopyClick(object sender, RoutedEventArgs e) =>
        AppSettings.AutoCopyLink = AutoCopyToggle.IsChecked == true;

    private void OnOpenLinkClick(object sender, RoutedEventArgs e) =>
        AppSettings.OpenLinkAfterRecording = OpenLinkToggle.IsChecked == true;

    private void OnKeepLocalClick(object sender, RoutedEventArgs e)
    {
        AppSettings.KeepLocalCopy = KeepLocalToggle.IsChecked == true;
        UpdateLocRowVisibility();
    }

    private void UpdateLocRowVisibility()
    {
        var vis = KeepLocalToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        LocRow.Visibility = vis;
        LocRowSep.Visibility = vis;
    }

    private void OnChangeFolderClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose where local clips are saved",
        };
        try { dlg.InitialDirectory = Directory.Exists(AppSettings.OutputFolder) ? AppSettings.OutputFolder : AppSettings.DefaultOutputDirectory; }
        catch { /* ignore */ }

        if (dlg.ShowDialog(this) != true) return;

        var folder = dlg.FolderName;
        if (!IsWritable(folder))
        {
            MessageBox.Show(this, "That folder can't be written to. Please choose another.",
                "WipShare", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        AppSettings.OutputFolder = folder;
        SetLocPath(folder);
        _logger.LogInformation("Output folder changed");
    }

    private void SetLocPath(string folder)
    {
        LocPath.Text = folder;
        LocPath.ToolTip = folder;
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".wipshare_write_test.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ----- connection -----

    private void RenderConnection()
    {
        bool has = _accessConfig.HasCode;
        ConnDot.Fill = (Brush)FindResource(has ? "OkBrush" : "WarnBrush");
        ConnLabel.Text = has ? "Connected" : "Local only";
        ConnSub.Text = has
            ? "Sharing is on - clips upload and you get a link."
            : "Add an invite code to upload and share links.";
        ConnAction.Content = has ? "Change" : "Set up";
    }

    private void OnConnActionClick(object sender, RoutedEventArgs e)
    {
        try { _openFirstRun(); }
        catch (Exception ex) { _logger.LogError(ex, "Opening first-run from Settings failed"); }
    }

    // ----- general -----

    private void OnStartupClick(object sender, RoutedEventArgs e)
    {
        bool want = StartupToggle.IsChecked == true;
        if (!StartupRegistry.SetEnabled(want))
        {
            // revert the toggle to the real registry state
            StartupToggle.IsChecked = StartupRegistry.IsEnabled();
            MessageBox.Show(this, "Couldn't update the startup setting.",
                "WipShare", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ----- helpers -----

    private void ShowFeedback(bool ok, string message)
    {
        HkFeedback.Visibility = Visibility.Visible;
        var brush = (Brush)FindResource(ok ? "OkBrush" : "ErrBrush");
        HkFeedbackDot.Fill = brush;
        HkFeedbackText.Foreground = brush;
        HkFeedbackText.Text = message;
    }

    private void ClearFeedback()
    {
        HkFeedback.Visibility = Visibility.Collapsed;
        HkFeedbackText.Text = "";
    }

    private void AutoClearFeedback()
    {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        t.Tick += (_, _) => { t.Stop(); if (_mode == HkMode.Default) ClearFeedback(); };
        t.Start();
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System or Key.None;

    private static string DescribeMods(ModifierKeys mods)
    {
        var parts = new List<string>(4);
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        return string.Join(" + ", parts);
    }

    private static List<string> ComboParts(ModifierKeys mods, Key key)
    {
        var parts = new List<string>(4);
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(HotkeyManager.KeyName(key));
        return parts;
    }

    private void RenderCombo(StackPanel panel, ModifierKeys mods, Key key)
    {
        panel.Children.Clear();
        var parts = ComboParts(mods, key);
        for (int i = 0; i < parts.Count; i++)
        {
            var chip = new Border { Style = (System.Windows.Style)FindResource("KbdChip") };
            chip.Child = new TextBlock { Style = (System.Windows.Style)FindResource("KbdText"), Text = parts[i] };
            panel.Children.Add(chip);
            if (i < parts.Count - 1)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "+",
                    Margin = new Thickness(4, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = (FontFamily)FindResource("MonoFont"),
                    FontSize = 11,
                    Foreground = (Brush)FindResource("Fg3Brush"),
                });
            }
        }
    }

    private static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, ancestor)) return true;
            node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }
}
