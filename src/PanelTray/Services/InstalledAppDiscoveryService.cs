using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PanelTray.Models;

namespace PanelTray.Services;

public sealed class InstalledAppDiscoveryService
{
    private static readonly string[] ShortcutRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
    ];

    public async Task<IReadOnlyList<InstalledAppCandidate>> DiscoverAsync(
        IEnumerable<string> existingLaunchKeys,
        CancellationToken cancellationToken = default)
    {
        var existing = existingLaunchKeys
            .Select(NormalizeLaunchKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var shortcuts = DiscoverShortcuts(existing);
        var shortcutNames = shortcuts
            .Select(app => app.DisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var storeApps = await DiscoverStoreAppsAsync(shortcutNames, existing, cancellationToken);

        return shortcuts
            .Concat(storeApps)
            .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static List<InstalledAppCandidate> DiscoverShortcuts(HashSet<string> existing)
    {
        var results = new List<InstalledAppCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in ShortcutRoots.Where(Directory.Exists))
        {
            foreach (var shortcutPath in EnumerateShortcutFiles(root))
            {
                if (!seen.Add(shortcutPath))
                {
                    continue;
                }

                if (!ShortcutImportService.CanImport(shortcutPath))
                {
                    continue;
                }

                var displayName = Path.GetFileNameWithoutExtension(shortcutPath);
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                var launchKey = NormalizeLaunchKey(shortcutPath);
                if (existing.Contains(launchKey))
                {
                    continue;
                }

                results.Add(new InstalledAppCandidate
                {
                    DisplayName = displayName,
                    LaunchPath = shortcutPath,
                    ShortcutPath = shortcutPath,
                    Source = InstalledAppSource.Shortcut
                });
            }
        }

        return results;
    }

    private static async Task<IReadOnlyList<InstalledAppCandidate>> DiscoverStoreAppsAsync(
        HashSet<string> shortcutNames,
        HashSet<string> existing,
        CancellationToken cancellationToken)
    {
        var output = await RunGetStartAppsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<InstalledAppCandidate>();
        }

        var apps = ParseStartAppsJson(output);
        var results = new List<InstalledAppCandidate>();

        foreach (var (name, appId) in apps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appId))
            {
                continue;
            }

            if (shortcutNames.Contains(name))
            {
                continue;
            }

            var launchPath = $"shell:AppsFolder\\{appId}";
            var launchKey = NormalizeLaunchKey(launchPath);
            if (existing.Contains(launchKey))
            {
                continue;
            }

            if (ShouldSkipStoreApp(name, appId))
            {
                continue;
            }

            results.Add(new InstalledAppCandidate
            {
                DisplayName = name.Trim(),
                LaunchPath = launchPath,
                AppUserModelId = appId,
                Source = InstalledAppSource.MicrosoftStore
            });
        }

        return results;
    }

    private static bool ShouldSkipStoreApp(string name, string appId)
    {
        if (name.StartsWith("Windows ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return appId.Contains("immersivecontrolpanel", StringComparison.OrdinalIgnoreCase)
            || appId.Contains("Windows.PrintDialog", StringComparison.OrdinalIgnoreCase)
            || appId.Contains("Windows.Search", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> RunGetStartAppsAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -STA -Command \"[Console]::OutputEncoding=[System.Text.UTF8Encoding]::UTF8; Get-StartApps | ConvertTo-Json -Compress\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return null;
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return await outputTask;
    }

    private static IReadOnlyList<(string Name, string AppId)> ParseStartAppsJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.EnumerateArray()
                    .Select(ReadStartApp)
                    .Where(item => item is not null)
                    .Select(item => item!.Value)
                    .ToArray(),
                JsonValueKind.Object => ReadStartApp(document.RootElement) is { } single
                    ? [single]
                    : Array.Empty<(string, string)>(),
                _ => Array.Empty<(string, string)>()
            };
        }
        catch (JsonException)
        {
            return Array.Empty<(string, string)>();
        }
    }

    private static (string Name, string AppId)? ReadStartApp(JsonElement element)
    {
        if (!element.TryGetProperty("Name", out var nameElement)
            || !element.TryGetProperty("AppID", out var appIdElement))
        {
            return null;
        }

        var name = nameElement.GetString()?.Trim();
        var appId = appIdElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appId))
        {
            return null;
        }

        return (name, appId);
    }

    private static IEnumerable<string> EnumerateShortcutFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(current, "*.lnk");
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                pending.Push(directory);
            }
        }
    }

    public static string NormalizeLaunchKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Environment.ExpandEnvironmentVariables(path.Trim()).TrimEnd('\\');
    }
}
