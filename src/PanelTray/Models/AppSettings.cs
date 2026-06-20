namespace PanelTray.Models;

public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    public int IconSize { get; set; } = 36;
    public bool ShowNames { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool HideOnFocusLost { get; set; }
    public double PanelWidth { get; set; } = 420;
    public double PanelHeight { get; set; } = 540;
    public bool EditMode { get; set; }
    public string HotkeyText { get; set; } = "Ctrl+Alt+P";
}
