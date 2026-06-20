namespace PanelTray.Services;

public static class ShortcutResolver
{
    private static readonly Dictionary<string, string[]> KnownExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ChatGPT"] =
        [
            @"%LOCALAPPDATA%\Microsoft\WindowsApps\ChatGPT.exe",
            @"%LOCALAPPDATA%\Programs\ChatGPT\ChatGPT.exe",
            @"%LOCALAPPDATA%\ChatGPT\ChatGPT.exe",
            @"%ProgramFiles%\ChatGPT\ChatGPT.exe"
        ],
        ["Discord"] =
        [
            @"%LOCALAPPDATA%\Discord\Update.exe",
            @"%LOCALAPPDATA%\Discord\app-1.0.9038\Discord.exe"
        ],
        ["WhatsApp"] =
        [
            @"%LOCALAPPDATA%\WhatsApp\WhatsApp.exe",
            @"%ProgramFiles%\WindowsApps\WhatsAppDesktop*\WhatsApp.exe"
        ],
        ["Spotify"] =
        [
            @"%APPDATA%\Spotify\Spotify.exe"
        ],
        ["NordVPN"] =
        [
            @"%ProgramFiles%\NordVPN\NordVPN.exe"
        ],
        ["Bitdefender"] =
        [
            @"%ProgramFiles%\Bitdefender\Bitdefender Security App\seccenter.exe",
            @"%ProgramFiles%\Bitdefender\Bitdefender Security App\bdagent.exe",
            @"%ProgramFiles%\Bitdefender\Bitdefender Security\bdagent.exe"
        ],
        ["Tailscale"] =
        [
            @"%ProgramFiles%\Tailscale\tailscale-ipn.exe"
        ]
    };

    public static string? FindKnownExecutable(string displayName)
    {
        if (!KnownExecutables.TryGetValue(displayName, out var candidates))
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            foreach (var expanded in ExpandKnownPathPatterns(candidate))
            {
                if (File.Exists(expanded))
                {
                    return expanded;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> ExpandKnownPathPatterns(string pattern)
    {
        var expanded = Environment.ExpandEnvironmentVariables(pattern);
        if (!expanded.Contains('*'))
        {
            yield return expanded;
            yield break;
        }

        var directory = Path.GetDirectoryName(expanded);
        var filePattern = Path.GetFileName(expanded);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory, filePattern, SearchOption.AllDirectories))
        {
            yield return file;
        }
    }

    public static string? FindShortcut(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            var shortcuts = EnumerateShortcutFiles(root).ToArray();
            var exact = shortcuts
                .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path)
                    .Equals(displayName, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }

            var partial = shortcuts
                .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path)
                    .Contains(displayName, StringComparison.OrdinalIgnoreCase));
            if (partial is not null)
            {
                return partial;
            }
        }

        return null;
    }

    public static string? ResolveIconSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (expanded.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveShortcutTarget(expanded) ?? expanded;
        }

        if (File.Exists(expanded))
        {
            return expanded;
        }

        if (expanded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return expanded;
        }

        var shortcut = FindShortcut(Path.GetFileNameWithoutExtension(expanded));
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            return ResolveShortcutTarget(shortcut) ?? shortcut;
        }

        return null;
    }

    public static string? ResolveShortcutTarget(string shortcutPath)
        => TryReadShortcut(shortcutPath, out var details) ? details.TargetPath : null;

    /// <summary>
    /// Resolves the icon source for a shortcut: custom IconLocation when set, otherwise the target exe.
    /// Never returns a .lnk path.
    /// </summary>
    public static string? ResolveShortcutIconPath(string shortcutPath)
    {
        if (!TryReadShortcut(shortcutPath, out var details))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(details.IconLocation))
        {
            var (iconFile, _) = ParseIconReference(details.IconLocation);
            if (File.Exists(Environment.ExpandEnvironmentVariables(iconFile)))
            {
                return details.IconLocation;
            }
        }

        return !string.IsNullOrWhiteSpace(details.TargetPath) && File.Exists(details.TargetPath)
            ? details.TargetPath
            : null;
    }

    public static (string FilePath, int Index) ParseIconReference(string iconReference)
    {
        if (string.IsNullOrWhiteSpace(iconReference))
        {
            return (string.Empty, 0);
        }

        var expanded = Environment.ExpandEnvironmentVariables(iconReference.Trim().Trim('"'));
        var commaIndex = expanded.LastIndexOf(',');
        if (commaIndex > 0
            && commaIndex < expanded.Length - 1
            && int.TryParse(expanded[(commaIndex + 1)..], out var index)
            && index >= 0)
        {
            return (expanded[..commaIndex], index);
        }

        return (expanded, 0);
    }

    private static bool TryReadShortcut(string shortcutPath, out ShortcutDetails details)
    {
        details = default;
        if (!File.Exists(shortcutPath))
        {
            return false;
        }

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return false;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            var target = ((string?)shortcut.TargetPath)?.Trim() ?? string.Empty;
            var iconLocation = ((string?)shortcut.IconLocation)?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(target) && string.IsNullOrWhiteSpace(iconLocation))
            {
                return false;
            }

            details = new ShortcutDetails(target, iconLocation);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct ShortcutDetails(string TargetPath, string IconLocation);

    public static string? ResolveLauncherIconPath(string executablePath)
    {
        var expanded = Environment.ExpandEnvironmentVariables(executablePath);
        if (!expanded.EndsWith("Update.exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(expanded);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var appFolder = Path.GetDirectoryName(directory);
        if (string.IsNullOrWhiteSpace(appFolder))
        {
            return null;
        }

        var candidates = Directory.GetFiles(appFolder, "*.exe", SearchOption.TopDirectoryOnly)
            .Where(file => !file.EndsWith("Update.exe", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.Contains("Discord", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return candidates.FirstOrDefault();
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
}
