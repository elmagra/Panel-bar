using PanelTray.Models;

namespace PanelTray.Services;

public static class InstalledAppImportService
{
    private static readonly (Func<string, string, bool> Match, string ProcessName)[] KnownStoreProcesses =
    [
        ((name, id) => name.Contains("Minecraft", StringComparison.OrdinalIgnoreCase)
            || id.Contains("Minecraft", StringComparison.OrdinalIgnoreCase), "Minecraft.Windows"),
        ((name, id) => name.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)
            || id.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase), "WhatsApp"),
        ((name, id) => name.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase)
            || id.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase), "ChatGPT"),
        ((name, id) => name.Contains("Spotify", StringComparison.OrdinalIgnoreCase)
            || id.Contains("Spotify", StringComparison.OrdinalIgnoreCase), "Spotify"),
        ((name, id) => name.Contains("Discord", StringComparison.OrdinalIgnoreCase)
            || id.Contains("Discord", StringComparison.OrdinalIgnoreCase), "Discord")
    ];

    public static AppEntry? CreateAppEntry(InstalledAppCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.ShortcutPath))
        {
            return ShortcutImportService.TryCreateApp(candidate.ShortcutPath);
        }

        if (candidate.Source == InstalledAppSource.MicrosoftStore
            && !string.IsNullOrWhiteSpace(candidate.LaunchPath))
        {
            var entry = new AppEntry
            {
                DisplayName = candidate.DisplayName,
                ExecutablePath = candidate.LaunchPath,
                IconPath = ResolveStoreIconPath(candidate),
                ProcessName = InferStoreProcessName(candidate.DisplayName, candidate.AppUserModelId ?? string.Empty),
                Enabled = true
            };

            ProcessInferenceService.Apply(entry);
            return entry;
        }

        return ShortcutImportService.TryCreateApp(candidate.LaunchPath);
    }

    private static string ResolveStoreIconPath(InstalledAppCandidate candidate)
    {
        var appId = StartAppsCatalog.ResolveAppUserModelId(
            candidate.DisplayName,
            candidate.LaunchPath,
            candidate.AppUserModelId);

        var packageLogo = PackageIconResolver.FindPackageLogoPath(
            appId ?? candidate.AppUserModelId ?? candidate.LaunchPath,
            candidate.DisplayName);
        if (!string.IsNullOrWhiteSpace(packageLogo))
        {
            return packageLogo;
        }

        return StoreAppIconResolver.ResolveIconPath(
            candidate.DisplayName,
            candidate.LaunchPath,
            candidate.AppUserModelId);
    }

    private static string InferStoreProcessName(string displayName, string appUserModelId)
    {
        foreach (var (match, processName) in KnownStoreProcesses)
        {
            if (match(displayName, appUserModelId))
            {
                return processName;
            }
        }

        return string.Empty;
    }
}
