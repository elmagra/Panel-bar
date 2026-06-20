using PanelTray.Models;

namespace PanelTray.Services;

public interface IConfigService
{
    string ConfigDirectory { get; }
    string ConfigPath { get; }
    RootConfig Current { get; }

    RootConfig Load();
    void Save();
    void SaveDebounced();
    void FlushSave();
}
