using PanelTray.Models;

namespace PanelTray.Services;

public static class ShortcutImportService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk",
        ".exe",
        ".url"
    };

    public static bool CanImport(string path)
        => TryCreateApp(path) is not null;

    public static AppEntry? TryCreateApp(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (!File.Exists(expanded))
        {
            return null;
        }

        var extension = Path.GetExtension(expanded);
        if (!SupportedExtensions.Contains(extension))
        {
            return null;
        }

        return extension.ToLowerInvariant() switch
        {
            ".lnk" => CreateFromShortcut(expanded),
            ".exe" => CreateFromExecutable(expanded, Path.GetFileNameWithoutExtension(expanded)),
            ".url" => CreateFromUrlShortcut(expanded),
            _ => null
        };
    }

    private static AppEntry? CreateFromShortcut(string shortcutPath)
    {
        if (!TryReadShortcut(shortcutPath, out var targetPath, out var arguments))
        {
            return null;
        }

        var displayName = Path.GetFileNameWithoutExtension(shortcutPath);
        var launchPath = File.Exists(targetPath) || Directory.Exists(targetPath) || IsUriLaunch(targetPath)
            ? shortcutPath
            : targetPath;

        var entry = new AppEntry
        {
            DisplayName = displayName,
            ExecutablePath = launchPath,
            IconPath = ResolveIconPath(shortcutPath, targetPath),
            Arguments = arguments ?? string.Empty,
            ProcessName = DeriveProcessName(targetPath),
            Enabled = true
        };

        ProcessInferenceService.Apply(entry, targetPath);
        return entry;
    }

    private static string ResolveIconPath(string shortcutPath, string targetPath)
    {
        var fromShortcut = ShortcutResolver.ResolveShortcutIconPath(shortcutPath);
        if (!string.IsNullOrWhiteSpace(fromShortcut))
        {
            return fromShortcut;
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(targetPath);
        return File.Exists(expanded) ? expanded : string.Empty;
    }

    private static AppEntry CreateFromExecutable(string executablePath, string displayName)
    {
        var entry = new AppEntry
        {
            DisplayName = displayName,
            ExecutablePath = executablePath,
            ProcessName = Path.GetFileNameWithoutExtension(executablePath),
            Enabled = true
        };

        ProcessInferenceService.Apply(entry, executablePath);
        return entry;
    }

    private static AppEntry? CreateFromUrlShortcut(string urlPath)
    {
        try
        {
            foreach (var line in File.ReadLines(urlPath))
            {
                if (!line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var url = line[4..].Trim();
                if (string.IsNullOrWhiteSpace(url))
                {
                    return null;
                }

                var displayName = Path.GetFileNameWithoutExtension(urlPath);
                return new AppEntry
                {
                    DisplayName = displayName,
                    ExecutablePath = url,
                    ProcessName = string.Empty,
                    Enabled = true
                };
            }
        }
        catch (IOException)
        {
            return null;
        }

        return null;
    }

    private static bool TryReadShortcut(string shortcutPath, out string targetPath, out string? arguments)
    {
        targetPath = string.Empty;
        arguments = null;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return false;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            targetPath = ((string?)shortcut.TargetPath)?.Trim() ?? string.Empty;
            arguments = ((string?)shortcut.Arguments)?.Trim();
            return !string.IsNullOrWhiteSpace(targetPath);
        }
        catch
        {
            return false;
        }
    }

    private static string DeriveProcessName(string targetPath)
    {
        if (IsUriLaunch(targetPath))
        {
            return string.Empty;
        }

        if (targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(targetPath);
        }

        return Path.GetFileNameWithoutExtension(targetPath);
    }

    private static bool IsUriLaunch(string path)
        => Uri.TryCreate(path, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "minecraft";
}
