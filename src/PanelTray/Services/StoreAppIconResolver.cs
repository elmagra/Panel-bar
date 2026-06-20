namespace PanelTray.Services;

public static class StoreAppIconResolver
{
    public static string? ParseAppUserModelId(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        const string prefix = "shell:AppsFolder\\";
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return path[prefix.Length..];
        }

        return path.Contains('!', StringComparison.Ordinal) ? path : null;
    }

    public static string ResolveIconPath(string displayName, string? executablePath, string? appUserModelId = null)
    {
        var shortcut = ShortcutResolver.FindShortcut(displayName);
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            var shortcutIcon = ShortcutResolver.ResolveShortcutIconPath(shortcut);
            if (!string.IsNullOrWhiteSpace(shortcutIcon))
            {
                return shortcutIcon;
            }
        }

        var known = ShortcutResolver.FindKnownExecutable(displayName);
        if (!string.IsNullOrWhiteSpace(known) && File.Exists(Environment.ExpandEnvironmentVariables(known)))
        {
            return known;
        }

        if (!string.IsNullOrWhiteSpace(executablePath)
            && executablePath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return executablePath;
        }

        if (!string.IsNullOrWhiteSpace(appUserModelId))
        {
            return $"shell:AppsFolder\\{appUserModelId}";
        }

        var packageLogo = PackageIconResolver.FindPackageLogoPath(executablePath, displayName);
        if (!string.IsNullOrWhiteSpace(packageLogo))
        {
            return packageLogo;
        }

        return string.Empty;
    }

    public static IEnumerable<string> GetModernAppIconCandidates(string displayName, string? executablePath, string? iconPath = null)
    {
        var appId = StartAppsCatalog.ResolveAppUserModelId(displayName, executablePath, iconPath);
        if (!string.IsNullOrWhiteSpace(appId))
        {
            var packageLogo = PackageIconResolver.FindPackageLogoPath(appId, displayName);
            if (!string.IsNullOrWhiteSpace(packageLogo))
            {
                yield return packageLogo;
            }

            yield return $"shell:AppsFolder\\{appId}";
            yield break;
        }

        if (IsShellIconPath(iconPath))
        {
            yield return iconPath!;
            yield break;
        }

        if (IsShellIconPath(executablePath))
        {
            yield return executablePath!;
        }
    }

    public static bool IsShellIconPath(string? path)
        => !string.IsNullOrWhiteSpace(path)
            && path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
}
