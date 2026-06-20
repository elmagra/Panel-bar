using System.Diagnostics;
using PanelTray.Interop;
using PanelTray.Models;

namespace PanelTray.Services;

public sealed class LauncherService : ILauncherService
{
    private readonly ILoggingService _logger;
    private readonly IProcessLearningService? _processLearningService;

    public LauncherService(ILoggingService logger, IProcessLearningService? processLearningService = null)
    {
        _logger = logger;
        _processLearningService = processLearningService;
    }

    public void OpenOrActivate(AppEntry app)
    {
        if (app.TrayIntegration == TrayIntegrationKind.Tailscale)
        {
            return;
        }

        if (!TryActivate(app))
        {
            Open(app);
        }
    }

    public void Open(AppEntry app)
    {
        if (string.IsNullOrWhiteSpace(app.ExecutablePath))
        {
            return;
        }

        try
        {
            var path = Expand(app.ExecutablePath);
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = app.Arguments ?? string.Empty,
                UseShellExecute = true,
                WorkingDirectory = File.Exists(path) ? Path.GetDirectoryName(path) : null
            };

            Process.Start(startInfo);
            _processLearningService?.ScheduleLearning(app);
            _logger.Info($"Opened {app.DisplayName}.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Could not open {app.DisplayName}.");
        }
    }

    public void Close(AppEntry app)
    {
        foreach (var process in GetMatchingProcesses(app, includeSecondary: false))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                    {
                        process.CloseMainWindow();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Could not close {app.DisplayName}.");
                }
            }
        }
    }

    public void CloseAll(AppEntry app)
    {
        foreach (var process in GetMatchingProcesses(app, includeSecondary: true))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Could not force close {app.DisplayName}.");
                }
            }
        }
    }

    public void Restart(AppEntry app)
    {
        CloseAll(app);
        Task.Delay(800).ContinueWith(_ => Open(app));
    }

    public void OpenLocation(AppEntry app)
    {
        try
        {
            var path = Expand(app.ExecutablePath);
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
                return;
            }

            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Could not open location for {app.DisplayName}.");
        }
    }

    private bool TryActivate(AppEntry app)
    {
        foreach (var process in GetMatchingProcesses(app, includeSecondary: false))
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    NativeMethods.ShowWindow(process.MainWindowHandle, NativeMethods.SwRestore);
                    if (NativeMethods.SetForegroundWindow(process.MainWindowHandle))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Could not activate {app.DisplayName}.");
                }
            }
        }

        return false;
    }

    private static IEnumerable<Process> GetMatchingProcesses(AppEntry app, bool includeSecondary)
    {
        var seenIds = new HashSet<int>();
        foreach (var processName in GetProcessNames(app, includeSecondary))
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                if (seenIds.Add(process.Id))
                {
                    yield return process;
                }
                else
                {
                    process.Dispose();
                }
            }
        }
    }

    private static IEnumerable<string> GetProcessNames(AppEntry app, bool includeSecondary)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mainName = string.IsNullOrWhiteSpace(app.ProcessName)
            ? Path.GetFileNameWithoutExtension(Expand(app.ExecutablePath))
            : app.ProcessName;

        if (!string.IsNullOrWhiteSpace(mainName))
        {
            names.Add(mainName);
        }

        if (!includeSecondary)
        {
            return names;
        }

        foreach (var secondary in (app.SecondaryProcessNames ?? string.Empty)
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = secondary.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? secondary[..^4]
                : secondary;
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                names.Add(normalized);
            }
        }

        return names;
    }

    private static string Expand(string value) => Environment.ExpandEnvironmentVariables(value ?? string.Empty);
}
