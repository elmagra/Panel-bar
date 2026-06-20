using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PanelTray.Services;

namespace PanelTray.Views.Controls;

public partial class HotkeyCaptureBox : UserControl
{
    private const int ConfirmDelaySeconds = 3;

    public static readonly DependencyProperty HotkeyTextProperty =
        DependencyProperty.Register(
            nameof(HotkeyText),
            typeof(string),
            typeof(HotkeyCaptureBox),
            new FrameworkPropertyMetadata(HotkeyParser.DefaultHotkey, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHotkeyTextChanged));

    private bool _capturing;
    private Window? _hostWindow;
    private string _pendingHotkey = string.Empty;
    private int _secondsLeft;
    private DispatcherTimer? _confirmTimer;
    private DispatcherTimer? _countdownTimer;

    public HotkeyCaptureBox()
    {
        InitializeComponent();
        UpdateDisplay();
        Loaded += (_, _) => _hostWindow = Window.GetWindow(this);
        Unloaded += (_, _) => StopCapturing();
    }

    public string HotkeyText
    {
        get => (string)GetValue(HotkeyTextProperty);
        set => SetValue(HotkeyTextProperty, value);
    }

    private static void OnHotkeyTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HotkeyCaptureBox box)
        {
            box.UpdateDisplay();
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        BeginCapture();
        e.Handled = true;
    }

    private void OnInputGotFocus(object sender, RoutedEventArgs e)
    {
        if (!_capturing)
        {
            BeginCapture();
        }
    }

    private void BeginCapture()
    {
        StopTimers();
        _pendingHotkey = string.Empty;
        _secondsLeft = ConfirmDelaySeconds;
        _capturing = true;
        UpdateDisplay();

        _hostWindow ??= Window.GetWindow(this);
        if (_hostWindow is not null)
        {
            _hostWindow.PreviewKeyDown -= OnWindowPreviewKeyDown;
            _hostWindow.PreviewKeyDown += OnWindowPreviewKeyDown;
        }

        InputBox.Focus();
        Keyboard.Focus(InputBox);
    }

    private void StopCapturing()
    {
        StopTimers();
        _capturing = false;
        _pendingHotkey = string.Empty;

        if (_hostWindow is not null)
        {
            _hostWindow.PreviewKeyDown -= OnWindowPreviewKeyDown;
        }

        UpdateDisplay();
    }

    private void StopTimers()
    {
        if (_confirmTimer is not null)
        {
            _confirmTimer.Stop();
            _confirmTimer.Tick -= OnConfirmTimerTick;
            _confirmTimer = null;
        }

        if (_countdownTimer is not null)
        {
            _countdownTimer.Stop();
            _countdownTimer.Tick -= OnCountdownTimerTick;
            _countdownTimer = null;
        }
    }

    private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_capturing)
        {
            Dispatcher.BeginInvoke(() =>
            {
                InputBox.Focus();
                Keyboard.Focus(InputBox);
            }, DispatcherPriority.Input);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        => HandleKeyDown(e);

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturing)
        {
            HandleKeyDown(e);
        }
    }

    private void HandleKeyDown(KeyEventArgs e)
    {
        if (!_capturing)
        {
            return;
        }

        e.Handled = true;

        var key = GetRealKey(e);
        if (key == Key.Escape)
        {
            StopCapturing();
            Keyboard.ClearFocus();
            return;
        }

        if (key == Key.Enter)
        {
            TryConfirmNow();
            return;
        }

        var candidate = BuildHotkeyCandidate(key);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            DisplayText.Text = "Pulsa Ctrl/Alt/Shift + otra tecla...";
            return;
        }

        _pendingHotkey = candidate;

        if (_pendingHotkey.EndsWith("+...", StringComparison.Ordinal))
        {
            StopTimers();
            DisplayText.Text = $"{_pendingHotkey} — ahora pulsa la tecla final";
            DisplayText.Foreground = (Brush)FindResource("AccentBrush");
            RootBorder.BorderThickness = new Thickness(2);
            return;
        }

        RestartConfirmTimers();
        UpdateCaptureDisplay();
    }

    private void RestartConfirmTimers()
    {
        StopTimers();
        _secondsLeft = ConfirmDelaySeconds;

        _confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ConfirmDelaySeconds) };
        _confirmTimer.Tick += OnConfirmTimerTick;
        _confirmTimer.Start();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += OnCountdownTimerTick;
        _countdownTimer.Start();
    }

    private void OnCountdownTimerTick(object? sender, EventArgs e)
    {
        _secondsLeft = Math.Max(0, _secondsLeft - 1);
        UpdateCaptureDisplay();
    }

    private void OnConfirmTimerTick(object? sender, EventArgs e)
    {
        TryConfirmNow();
    }

    private void TryConfirmNow()
    {
        if (!_capturing)
        {
            return;
        }

        if (HotkeyParser.IsValid(_pendingHotkey))
        {
            HotkeyText = _pendingHotkey;
            StopCapturing();
            Keyboard.ClearFocus();
            return;
        }

        DisplayText.Text = "Combinacion invalida. Prueba otra (Esc cancelar)";
        StopTimers();
        _secondsLeft = ConfirmDelaySeconds;
    }

    private void UpdateCaptureDisplay()
    {
        if (!_capturing)
        {
            UpdateDisplay();
            return;
        }

        DisplayText.Foreground = (Brush)FindResource("AccentBrush");
        RootBorder.BorderThickness = new Thickness(2);

        if (string.IsNullOrWhiteSpace(_pendingHotkey))
        {
            DisplayText.Text = "Pulsa tu combinacion... (Esc cancelar)";
            return;
        }

        if (_confirmTimer is null)
        {
            DisplayText.Text = $"{_pendingHotkey} — pulsa Enter o espera";
            return;
        }

        DisplayText.Text = $"{_pendingHotkey} — confirma en {_secondsLeft}s (Enter / Esc)";
    }

    private void UpdateDisplay()
    {
        if (_capturing)
        {
            UpdateCaptureDisplay();
            return;
        }

        DisplayText.Text = string.IsNullOrWhiteSpace(HotkeyText)
            ? "Clic para asignar hotkey"
            : HotkeyText;
        DisplayText.Foreground = (Brush)FindResource("TextBrush");
        RootBorder.BorderThickness = new Thickness(1);
    }

    private string BuildHotkeyCandidate(Key key)
    {
        if (IsModifierKey(key))
        {
            var modifiersOnly = GetModifierNames(Keyboard.Modifiers);
            return modifiersOnly.Count == 0
                ? string.Empty
                : $"{string.Join("+", modifiersOnly)}+...";
        }

        var modifiers = GetModifierNames(Keyboard.Modifiers);
        if (modifiers.Count == 0)
        {
            return string.Empty;
        }

        modifiers.Add(HotkeyDisplayNames.FormatKey(key));
        return string.Join("+", modifiers);
    }

    private static Key GetRealKey(KeyEventArgs e)
    {
        return e.Key switch
        {
            Key.System or Key.ImeProcessed or Key.DeadCharProcessed => e.SystemKey,
            _ => e.Key
        };
    }

    private static bool IsModifierKey(Key key)
        => key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;

    private static List<string> GetModifierNames(ModifierKeys modifiers)
    {
        var names = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            names.Add("Ctrl");
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            names.Add("Alt");
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            names.Add("Shift");
        }

        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            names.Add("Win");
        }

        return names;
    }
}
