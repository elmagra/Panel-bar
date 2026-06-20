using PanelTray.Models;

namespace PanelTray.Services;

public interface ILauncherService
{
    void OpenOrActivate(AppEntry app);
    void Open(AppEntry app);
    void Close(AppEntry app);
    void CloseAll(AppEntry app);
    void Restart(AppEntry app);
    void OpenLocation(AppEntry app);
}
