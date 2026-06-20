using PanelTray.Models;

namespace PanelTray.Services;

public interface IProcessLearningService
{
    event EventHandler<AppEntry>? ProcessesLearned;

    void ScheduleLearning(AppEntry app);
}
