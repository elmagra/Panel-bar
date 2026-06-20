namespace PanelTray.Services;

public interface ITrayService : IDisposable
{
    event EventHandler? TogglePanelRequested;
    event EventHandler? SettingsRequested;
    event EventHandler? RestartRequested;
    event EventHandler? ExitRequested;

    void Initialize();
}
