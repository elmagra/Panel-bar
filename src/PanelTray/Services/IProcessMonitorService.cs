using PanelTray.Models;

namespace PanelTray.Services;

public interface IProcessMonitorService : IDisposable
{
    event EventHandler<IReadOnlyDictionary<Guid, AppRunState>>? StatusChanged;

    void SetApps(Func<IReadOnlyCollection<AppEntry>> appsProvider);
    void SetFastPolling(bool enabled);
    void Start();
    void RefreshNow();
}
