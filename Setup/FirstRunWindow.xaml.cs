using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.Logging;
using WipShare.Client.Config;

namespace WipShare.Client.Setup;

/// <summary>
/// First-run / "Change invite code" window. Four visual states (default,
/// verifying, error, success) matching the design. On a verified code it saves
/// the secret to Credential Manager (via <see cref="AccessConfig"/>), raises
/// <see cref="CodeAccepted"/>, and closes itself. Skip / Esc / close leave the
/// app in local-only mode.
/// </summary>
public partial class FirstRunWindow : Window, IDisposable
{
    private enum UiState { Default, Verifying, Error, Success }

    private readonly AccessConfig _accessConfig;
    private readonly ILogger _logger;
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _cts = new();

    private readonly Brush _errBrush;
    private readonly Brush _okBrush;
    private readonly Brush _fg2Brush;
    private readonly Brush _fg3Brush;
    private readonly Brush _bgBrush;

    private UiState _state = UiState.Default;
    private bool _busy;
    private bool _disposed;

    /// <summary>Raised once a code has been verified and saved.</summary>
    public event EventHandler? CodeAccepted;

    public FirstRunWindow(AccessConfig accessConfig, ILoggerFactory loggerFactory)
    {
        _accessConfig = accessConfig;
        _logger = loggerFactory.CreateLogger<FirstRunWindow>();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        InitializeComponent();

        _errBrush = (Brush)FindResource("ErrBrush");
        _okBrush = (Brush)FindResource("OkBrush");
        _fg2Brush = (Brush)FindResource("Fg2Brush");
        _fg3Brush = (Brush)FindResource("Fg3Brush");
        _bgBrush = (Brush)FindResource("BgBrush");

        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) =>
        {
            CodeBox.Focus();
            Keyboard.Focus(CodeBox);
        };

        // Clip content to the chrome's rounded corners so the close button's red
        // hover never spills past the corner (design overflow:hidden).
        ContentRoot.SizeChanged += (_, _) => UpdateCornerClip();
        UpdateCornerClip();
    }

    private void UpdateCornerClip() =>
        ContentRoot.Clip = new RectangleGeometry(
            new Rect(0, 0, ContentRoot.ActualWidth, ContentRoot.ActualHeight), 7, 7);

    // ----- input / chrome -----

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch { /* not in a drag-able state */ }
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter && !_busy)
        {
            e.Handled = true;
            _ = TryConnectAsync();
        }
    }

    private void OnCodeChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        Placeholder.Visibility = string.IsNullOrEmpty(CodeBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (_state == UiState.Error)
        {
            // As soon as they start correcting the code, clear the error styling.
            CodeBox.Tag = null;
            StatusPanel.Visibility = Visibility.Collapsed;
            _state = UiState.Default;
        }
    }

    private void OnConnectClick(object sender, RoutedEventArgs e) => _ = TryConnectAsync();

    private void OnSkipClick(object sender, RoutedEventArgs e) => Close();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // ----- connect flow -----

    private async Task TryConnectAsync()
    {
        if (_busy) return;

        var code = CodeBox.Text?.Trim() ?? string.Empty;
        if (code.Length == 0)
        {
            SetError("Enter your invite code.");
            return;
        }

        SetVerifying();

        bool ok;
        try
        {
            ok = await VerifyAsync(code, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            return; // window closing
        }
        catch (VerifyException vex)
        {
            SetError(vex.Message);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invite-code verify request failed");
            SetError("Couldn't reach WipShare. Check your connection.");
            return;
        }

        if (!ok)
        {
            SetError("That code didn't work. Check it and try again.");
            return;
        }

        try
        {
            _accessConfig.SaveCode(code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving invite code to Credential Manager failed");
            SetError("Couldn't save the code on this PC.");
            return;
        }

        SetSuccess();
        CodeAccepted?.Invoke(this, EventArgs.Empty);

        try { await Task.Delay(950, _cts.Token); }
        catch (OperationCanceledException) { return; }

        Close();
    }

    private async Task<bool> VerifyAsync(string code, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new { code });
        using var req = new HttpRequestMessage(
            HttpMethod.Post, Constants.UploadBaseUrl.TrimEnd('/') + "/api/access/verify")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        using var resp = await _http.SendAsync(req, ct);

        if (resp.StatusCode == HttpStatusCode.OK) return true;
        if (resp.StatusCode == HttpStatusCode.Unauthorized) return false;
        if ((int)resp.StatusCode == 429)
            throw new VerifyException("Too many attempts. Wait a minute and try again.");
        throw new VerifyException($"WipShare returned an unexpected error ({(int)resp.StatusCode}).");
    }

    // ----- state painting -----

    private void SetVerifying()
    {
        _state = UiState.Verifying;
        _busy = true;
        CodeBox.IsEnabled = false;
        CodeBox.Tag = null;
        ConnectButton.IsEnabled = false;
        SkipButton.IsEnabled = false;
        ConnectCheck.Visibility = Visibility.Collapsed;
        ConnectSpinner.Visibility = Visibility.Visible;
        StartSpinner();
        ConnectLabel.Text = "Connecting…";
        StatusPanel.Visibility = Visibility.Visible;
        StatusDot.Visibility = Visibility.Collapsed;
        StatusText.Text = "Checking your code…";
        StatusText.Foreground = _fg2Brush;
    }

    private void SetError(string message)
    {
        _state = UiState.Error;
        _busy = false;
        StopSpinner();
        CodeBox.IsEnabled = true;
        CodeBox.Tag = "error";
        ConnectButton.IsEnabled = true;
        SkipButton.IsEnabled = true;
        ConnectSpinner.Visibility = Visibility.Collapsed;
        ConnectCheck.Visibility = Visibility.Collapsed;
        ConnectLabel.Text = "Connect";
        StatusPanel.Visibility = Visibility.Visible;
        StatusDot.Visibility = Visibility.Visible;
        StatusDot.Fill = _errBrush;
        StatusText.Text = message;
        StatusText.Foreground = _errBrush;
        CodeBox.Focus();
        CodeBox.SelectAll();
    }

    private void SetSuccess()
    {
        _state = UiState.Success;
        _busy = true;
        StopSpinner();
        CodeBox.IsEnabled = false;
        ConnectButton.IsEnabled = false;
        SkipButton.IsEnabled = false;
        ConnectSpinner.Visibility = Visibility.Collapsed;
        ConnectCheck.Visibility = Visibility.Visible;
        ConnectLabel.Text = "Connected";
        ConnectLabel.Foreground = _bgBrush;
        ConnectButton.Background = _okBrush;
        ConnectButton.BorderBrush = _okBrush;
        // calm the form before the window closes itself
        CodeBox.Opacity = 0.4;
        SkipButton.Opacity = 0.4;
        StatusPanel.Visibility = Visibility.Visible;
        StatusDot.Visibility = Visibility.Visible;
        StatusDot.Fill = _okBrush;
        StatusText.Text = "Connected · you're all set";
        StatusText.Foreground = _okBrush;
    }

    private void StartSpinner()
    {
        var anim = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.7)))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        SpinAngle.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    private void StopSpinner() => SpinAngle.BeginAnimation(RotateTransform.AngleProperty, null);

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { /* ignore */ }
        _cts.Dispose();
        _http.Dispose();
    }

    private sealed class VerifyException : Exception
    {
        public VerifyException(string message) : base(message) { }
    }
}
