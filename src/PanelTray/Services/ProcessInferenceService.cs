using System.Text.RegularExpressions;
using PanelTray.Models;

namespace PanelTray.Services;

public static class ProcessInferenceService
{
    private static readonly Regex ProcessStartPattern = new(
        @"--processStart\s+(?:""([^""]+)""|(\S+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RiotProductPattern = new(
        @"--launch-product=(\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Dictionary<string, (string Main, string Secondary)> RiotProducts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["valorant"] = ("VALORANT-Win64-Shipping", "RiotClientServices"),
            ["league_of_legends"] = ("League of Legends", "RiotClientServices"),
            ["bacon"] = ("VALORANT-Win64-Shipping", "RiotClientServices")
        };

    private static readonly (Func<string, string, bool> Match, string Main, string Secondary)[] KnownProfiles =
    [
        (MatchEpic, "EpicGamesLauncher", "EpicWebHelper"),
        (MatchNordVpn, "NordVPN", "nordvpn-service"),
        (MatchBitdefender, "seccenter", "bdagent"),
        (MatchTailscale, "tailscale-ipn", "tailscale"),
        (MatchDiscord, "Discord", "Update")
    ];

    public static void Apply(AppEntry entry, string? shortcutTargetPath = null)
    {
        var targetPath = ResolveTargetPath(entry, shortcutTargetPath);
        var launcherName = GetProcessBaseName(targetPath);
        var args = entry.Arguments ?? string.Empty;

        if (TryInferFromProcessStart(args, launcherName, out var main, out var secondary))
        {
            entry.ProcessName = main;
            entry.SecondaryProcessNames = secondary;
            return;
        }

        if (TryInferFromRiot(args, targetPath, out main, out secondary))
        {
            entry.ProcessName = main;
            entry.SecondaryProcessNames = secondary;
            return;
        }

        if (TryInferKnownProfile(entry.DisplayName, targetPath, out main, out secondary))
        {
            entry.ProcessName = main;
            if (!string.IsNullOrWhiteSpace(secondary))
            {
                entry.SecondaryProcessNames = secondary;
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(entry.ProcessName) && !string.IsNullOrWhiteSpace(launcherName))
        {
            entry.ProcessName = launcherName;
        }

        EnsureLauncherSecondary(entry, launcherName);
    }

    private static bool TryInferFromProcessStart(
        string arguments,
        string launcherName,
        out string mainProcess,
        out string secondaryProcesses)
    {
        mainProcess = string.Empty;
        secondaryProcesses = string.Empty;

        var match = ProcessStartPattern.Match(arguments);
        if (!match.Success)
        {
            return false;
        }

        var startedExe = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        mainProcess = GetProcessBaseName(startedExe);
        if (string.IsNullOrWhiteSpace(mainProcess))
        {
            return false;
        }

        secondaryProcesses = !string.IsNullOrWhiteSpace(launcherName)
            && !launcherName.Equals(mainProcess, StringComparison.OrdinalIgnoreCase)
            ? launcherName
            : string.Empty;

        return true;
    }

    private static bool TryInferFromRiot(
        string arguments,
        string? targetPath,
        out string mainProcess,
        out string secondaryProcesses)
    {
        mainProcess = string.Empty;
        secondaryProcesses = string.Empty;

        var target = targetPath ?? string.Empty;
        if (!target.Contains("RiotClientServices", StringComparison.OrdinalIgnoreCase)
            && !arguments.Contains("launch-product", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = RiotProductPattern.Match(arguments);
        if (!match.Success)
        {
            return false;
        }

        var product = match.Groups[1].Value.Trim().Trim('"');
        if (!RiotProducts.TryGetValue(product, out var profile))
        {
            return false;
        }

        mainProcess = profile.Main;
        secondaryProcesses = profile.Secondary;
        return true;
    }

    private static bool TryInferKnownProfile(
        string displayName,
        string? targetPath,
        out string mainProcess,
        out string secondaryProcesses)
    {
        mainProcess = string.Empty;
        secondaryProcesses = string.Empty;

        foreach (var profile in KnownProfiles)
        {
            if (!profile.Match(displayName, targetPath ?? string.Empty))
            {
                continue;
            }

            mainProcess = profile.Main;
            secondaryProcesses = profile.Secondary;
            return true;
        }

        return false;
    }

    private static void EnsureLauncherSecondary(AppEntry entry, string launcherName)
    {
        if (string.IsNullOrWhiteSpace(launcherName)
            || string.IsNullOrWhiteSpace(entry.ProcessName)
            || !string.IsNullOrWhiteSpace(entry.SecondaryProcessNames))
        {
            return;
        }

        if (launcherName.Equals(entry.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        entry.SecondaryProcessNames = launcherName;
    }

    private static string? ResolveTargetPath(AppEntry entry, string? shortcutTargetPath)
    {
        if (!string.IsNullOrWhiteSpace(shortcutTargetPath))
        {
            return Environment.ExpandEnvironmentVariables(shortcutTargetPath);
        }

        var path = Environment.ExpandEnvironmentVariables(entry.ExecutablePath ?? string.Empty);
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return ShortcutResolver.ResolveShortcutTarget(path);
        }

        return File.Exists(path) ? path : path;
    }

    private static string GetProcessBaseName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var fileName = Path.GetFileName(path.Trim().Trim('"'));
        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : Path.GetFileNameWithoutExtension(fileName);
    }

    private static bool MatchEpic(string displayName, string targetPath)
        => displayName.Contains("Epic", StringComparison.OrdinalIgnoreCase)
            || targetPath.Contains("EpicGamesLauncher", StringComparison.OrdinalIgnoreCase);

    private static bool MatchNordVpn(string displayName, string targetPath)
        => displayName.Contains("NordVPN", StringComparison.OrdinalIgnoreCase)
            || targetPath.Contains("NordVPN.exe", StringComparison.OrdinalIgnoreCase);

    private static bool MatchBitdefender(string displayName, string targetPath)
        => displayName.Contains("Bitdefender", StringComparison.OrdinalIgnoreCase)
            || targetPath.Contains("seccenter.exe", StringComparison.OrdinalIgnoreCase)
            || targetPath.Contains("bdagent.exe", StringComparison.OrdinalIgnoreCase);

    private static bool MatchTailscale(string displayName, string targetPath)
        => displayName.Contains("Tailscale", StringComparison.OrdinalIgnoreCase)
            || targetPath.Contains("tailscale-ipn.exe", StringComparison.OrdinalIgnoreCase);

    private static bool MatchDiscord(string displayName, string targetPath)
        => displayName.Contains("Discord", StringComparison.OrdinalIgnoreCase)
            || targetPath.EndsWith("Update.exe", StringComparison.OrdinalIgnoreCase);
}
