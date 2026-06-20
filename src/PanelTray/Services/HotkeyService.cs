using System.Windows;
using System.Windows.Interop;
using PanelTray.Interop;

namespace PanelTray.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 5001;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private readonly ILoggingService _logger;
    private HwndSource? _source;
    private IntPtr _handle;
    private Action? _callback;
    private bool _registered;

    public HotkeyService(ILoggingService logger)
    {
        _logger = logger;
    }

    public void Register(Window owner, string hotkeyText, Action callback)
    {
        _callback = callback;
        Unregister();

        if (!HotkeyParser.TryParse(hotkeyText, out var modifiers, out var virtualKey))
        {
            _logger.Warn($"Invalid hotkey: {hotkeyText}");
            return;
        }

        _handle = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);

        _registered = NativeMethods.RegisterHotKey(_handle, HotkeyId, modifiers | ModNoRepeat, virtualKey);
        if (!_registered)
        {
            _logger.Warn($"Could not register hotkey: {hotkeyText}");
        }
        else
        {
            _logger.Info($"Registered hotkey: {hotkeyText}");
        }
    }

    public void Unregister()
    {
        if (_registered && _handle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_handle, HotkeyId);
        }

        _source?.RemoveHook(WndProc);
        _registered = false;
        _source = null;
        _handle = IntPtr.Zero;
    }

    public void Dispose() => Unregister();

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _callback?.Invoke();
        }

        return IntPtr.Zero;
    }
}
