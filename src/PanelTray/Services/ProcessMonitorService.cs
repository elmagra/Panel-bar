using System.Diagnostics;
using PanelTray.Interop;
using PanelTray.Models;

namespace PanelTray.Services;

public sealed class ProcessMonitorService : IProcessMonitorService
{
    private readonly ILoggingService _logger;
    private readonly object _gate = new();
    private readonly System.Threading.Timer _timer;
    private Func<IReadOnlyCollection<AppEntry>> _appsProvider = () => Array.Empty<AppEntry>();
    private bool _fastPolling;

    public ProcessMonitorService(ILoggingService logger)
    {
        _logger = logger;
        _timer = new System.Threading.Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler<IReadOnlyDictionary<Guid, AppRunState>>? StatusChanged;

    public void SetApps(Func<IReadOnlyCollection<AppEntry>> appsProvider)
        => _appsProvider = appsProvider;

    public void SetFastPolling(bool enabled)
    {
        _fastPolling = enabled;
        if (enabled)
        {
            _timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        }
        else
        {
            _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
        }
    }

    public void Start() => SetFastPolling(false);

    public void RefreshNow() => Poll();

    public void Dispose() => _timer.Dispose();

    private void Poll()
    {
        if (!Monitor.TryEnter(_gate))
        {
            return;
        }

        try
        {
            var processes = Process.GetProcesses()
                .Select(process =>
                {
                    try
                    {
                        return new ProcessSnapshot(process.ProcessName, process.MainWindowHandle);
                    }
                    catch
                    {
                        return null;
                    }
                    finally
                    {
                        process.Dispose();
                    }
                })
                .Where(snapshot => snapshot is not null)
                .Cast<ProcessSnapshot>()
                .ToArray();

            var result = new Dictionary<Guid, AppRunState>();
            foreach (var app in _appsProvider())
            {
                result[app.Id] = DetectState(app, processes);
            }

            StatusChanged?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Process polling failed.");
        }
        finally
        {
            Monitor.Exit(_gate);
            _timer.Change(
                _fastPolling ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(10),
                _fastPolling ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(10));
        }
    }

    private static AppRunState DetectState(AppEntry app, ProcessSnapshot[] processes)
    {
        var mainName = GetMainProcessName(app);
        var secondaryNames = ParseSecondaryNames(app.SecondaryProcessNames);

        if (string.IsNullOrWhiteSpace(mainName) && secondaryNames.Count == 0)
        {
            return AppRunState.Closed;
        }

        var mainExists = false;
        var mainVisible = false;
        var secondaryExists = false;

        foreach (var snapshot in processes)
        {
            if (!string.IsNullOrWhiteSpace(mainName)
                && snapshot.Name.Equals(mainName, StringComparison.OrdinalIgnoreCase))
            {
                mainExists = true;
                if (HasVisibleMainWindow(snapshot.MainWindowHandle))
                {
                    mainVisible = true;
                }
            }
            else if (secondaryNames.Contains(snapshot.Name))
            {
                secondaryExists = true;
            }
        }

        if (mainVisible)
        {
            return AppRunState.Active;
        }

        if (mainExists || secondaryExists)
        {
            return AppRunState.Background;
        }

        return AppRunState.Closed;
    }

    private static bool HasVisibleMainWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return NativeMethods.IsWindowVisible(handle);
        }
        catch
        {
            return false;
        }
    }

    private static string GetMainProcessName(AppEntry app)
    {
        if (!string.IsNullOrWhiteSpace(app.ProcessName))
        {
            return CleanProcessName(app.ProcessName);
        }

        var path = Environment.ExpandEnvironmentVariables(app.ExecutablePath ?? string.Empty);
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var target = ShortcutResolver.ResolveShortcutTarget(path);
            if (!string.IsNullOrWhiteSpace(target))
            {
                return Path.GetFileNameWithoutExtension(target);
            }
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    private static HashSet<string> ParseSecondaryNames(string value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanProcessName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string CleanProcessName(string processName)
    {
        processName = processName.Trim();
        return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
    }

    private sealed record ProcessSnapshot(string Name, IntPtr MainWindowHandle);
}
