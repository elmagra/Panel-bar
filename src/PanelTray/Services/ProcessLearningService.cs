using System.Diagnostics;
using PanelTray.Models;

namespace PanelTray.Services;

public sealed class ProcessLearningService : IProcessLearningService
{
    private static readonly HashSet<string> IgnoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "conhost",
        "cmd",
        "powershell",
        "WerFault",
        "dllhost",
        "RuntimeBroker",
        "SearchHost",
        "ShellExperienceHost",
        "svchost",
        "backgroundTaskHost",
        "smartscreen",
        "ApplicationFrameHost",
        "sihost",
        "taskhostw",
        "ctfmon",
        "fontdrvhost",
        "audiodg",
        "WmiPrvSE",
        "sppsvc",
        "msiexec",
        "explorer",
        "PanelTray"
    };

    private readonly ILoggingService _logger;
    private readonly IConfigService _configService;
    private readonly HashSet<Guid> _activeLearning = new();

    public ProcessLearningService(ILoggingService logger, IConfigService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    public event EventHandler<AppEntry>? ProcessesLearned;

    public void ScheduleLearning(AppEntry app)
    {
        if (!CanLearn(app))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(app.SecondaryProcessNames))
        {
            return;
        }

        ProcessInferenceService.Apply(app);
        if (!string.IsNullOrWhiteSpace(app.SecondaryProcessNames))
        {
            _logger.Info($"Inferred processes for {app.DisplayName} without launch learning.");
            _configService.SaveDebounced();
            ProcessesLearned?.Invoke(this, app);
            return;
        }

        if (!_activeLearning.Add(app.Id))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var learned = await LearnFromLaunchAsync(app);
                if (learned is null)
                {
                    return;
                }

                ApplyLearned(app, learned);
                _configService.SaveDebounced();
                ProcessesLearned?.Invoke(this, app);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Process learning failed for {app.DisplayName}.");
            }
            finally
            {
                lock (_activeLearning)
                {
                    _activeLearning.Remove(app.Id);
                }
            }
        });
    }

    private static bool CanLearn(AppEntry app)
    {
        if (app.TrayIntegration != TrayIntegrationKind.None)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(app.ExecutablePath))
        {
            return false;
        }

        var path = Environment.ExpandEnvironmentVariables(app.ExecutablePath);
        return !Uri.TryCreate(path, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https");
    }

    private async Task<LearnedProcesses?> LearnFromLaunchAsync(AppEntry app)
    {
        var baseline = SnapshotProcessIds();
        var installRoots = GetInstallRoots(app);
        var launcherName = GetLauncherProcessName(app);
        var configuredMain = NormalizeProcessName(app.ProcessName);

        await Task.Delay(1500);

        for (var attempt = 0; attempt < 6; attempt++)
        {
            var newProcesses = GetNewProcesses(baseline)
                .Where(process => !IgnoredProcessNames.Contains(process.Name))
                .Where(process => IsRelated(process, installRoots, launcherName, configuredMain))
                .ToArray();

            if (newProcesses.Length > 0)
            {
                var learned = BuildResult(newProcesses, configuredMain, launcherName);
                if (learned is not null)
                {
                    _logger.Info(
                        $"Learned processes for {app.DisplayName}: main={learned.MainProcess}, secondary={learned.SecondaryProcesses}");
                    return learned;
                }
            }

            await Task.Delay(1000);
        }

        return null;
    }

    private static LearnedProcesses? BuildResult(
        ProcessSnapshot[] newProcesses,
        string configuredMain,
        string launcherName)
    {
        var grouped = newProcesses
            .GroupBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Name = group.Key,
                HasWindow = group.Any(process => process.HasWindow),
                Count = group.Count()
            })
            .OrderByDescending(item => item.HasWindow)
            .ThenByDescending(item => item.Count)
            .ToArray();

        if (grouped.Length == 0)
        {
            return null;
        }

        var mainName = !string.IsNullOrWhiteSpace(configuredMain)
            ? configuredMain
            : grouped.FirstOrDefault(item => item.HasWindow)?.Name ?? grouped[0].Name;

        var secondary = grouped
            .Select(item => item.Name)
            .Where(name => !name.Equals(mainName, StringComparison.OrdinalIgnoreCase))
            .Where(name => !name.Equals(launcherName, StringComparison.OrdinalIgnoreCase)
                || grouped.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (secondary.Length == 0)
        {
            if (string.IsNullOrWhiteSpace(configuredMain)
                && !string.IsNullOrWhiteSpace(mainName)
                && grouped.Length == 1)
            {
                return new LearnedProcesses(mainName, string.Empty);
            }

            return null;
        }

        return new LearnedProcesses(mainName, string.Join(", ", secondary));
    }

    private static void ApplyLearned(AppEntry app, LearnedProcesses learned)
    {
        if (string.IsNullOrWhiteSpace(app.ProcessName)
            && !string.IsNullOrWhiteSpace(learned.MainProcess))
        {
            app.ProcessName = learned.MainProcess;
        }

        if (!string.IsNullOrWhiteSpace(learned.SecondaryProcesses))
        {
            app.SecondaryProcessNames = learned.SecondaryProcesses;
        }
    }

    private static bool IsRelated(
        ProcessSnapshot process,
        IReadOnlyList<string> installRoots,
        string launcherName,
        string configuredMain)
    {
        if (process.Name.Equals(launcherName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(configuredMain)
            && process.Name.Equals(configuredMain, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(process.Path))
        {
            return false;
        }

        foreach (var root in installRoots)
        {
            if (process.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> GetInstallRoots(AppEntry app)
    {
        var roots = new List<string>();
        AddRoot(roots, ResolveLaunchTarget(app));

        if (roots.Count == 0)
        {
            return roots;
        }

        var parent = Path.GetDirectoryName(roots[0]);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            roots.Add(parent);
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddRoot(List<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            roots.Add(directory);
        }
    }

    private static string? ResolveLaunchTarget(AppEntry app)
    {
        var path = Environment.ExpandEnvironmentVariables(app.ExecutablePath ?? string.Empty);
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return ShortcutResolver.ResolveShortcutTarget(path);
        }

        return File.Exists(path) ? path : null;
    }

    private static string GetLauncherProcessName(AppEntry app)
    {
        var executablePath = Environment.ExpandEnvironmentVariables(app.ExecutablePath ?? string.Empty);
        if (executablePath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
            || executablePath.Contains("://", StringComparison.Ordinal)
            || executablePath.EndsWith(':'))
        {
            return string.Empty;
        }

        var target = ResolveLaunchTarget(app);
        if (string.IsNullOrWhiteSpace(target))
        {
            return NormalizeProcessName(Path.GetFileNameWithoutExtension(app.ExecutablePath));
        }

        return NormalizeProcessName(Path.GetFileName(target));
    }

    private static HashSet<int> SnapshotProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                ids.Add(process.Id);
            }
            catch
            {
                // Ignore inaccessible processes.
            }
            finally
            {
                process.Dispose();
            }
        }

        return ids;
    }

    private static List<ProcessSnapshot> GetNewProcesses(HashSet<int> baseline)
    {
        var processes = new List<ProcessSnapshot>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (baseline.Contains(process.Id))
                {
                    continue;
                }

                processes.Add(new ProcessSnapshot(
                    process.Id,
                    process.ProcessName,
                    TryGetProcessPath(process),
                    process.MainWindowHandle != IntPtr.Zero));
            }
            catch
            {
                // Ignore inaccessible processes.
            }
            finally
            {
                process.Dispose();
            }
        }

        return processes;
    }

    private static string? TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeProcessName(string? value)
    {
        value = (value ?? string.Empty).Trim();
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? value[..^4]
            : value;
    }

    private sealed record ProcessSnapshot(int Id, string Name, string? Path, bool HasWindow);

    private sealed record LearnedProcesses(string MainProcess, string SecondaryProcesses);
}
