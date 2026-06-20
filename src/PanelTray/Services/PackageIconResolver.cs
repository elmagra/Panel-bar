using System.Diagnostics;

namespace PanelTray.Services;

public static class PackageIconResolver
{
    private static readonly string[] LogoFileNames =
    [
        "Square44x44Logo.scale-400.png",
        "Square44x44Logo.scale-200.png",
        "Square44x44Logo.scale-150.png",
        "Square44x44Logo.scale-125.png",
        "Square44x44Logo.scale-100.png",
        "Square44x44Logo.png",
        "GraphicsLogo.png",
        "LargeLogo.png",
        "StoreLogo.scale-200.png",
        "StoreLogo.scale-100.png",
        "StoreLogo.png",
        "SmallLogo.png",
        "Logo.png",
        "AppList.png",
        "SplashScreen.scale-200.png"
    ];

    private static readonly Dictionary<string, string?> InstallLocationCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string?> LogoPathCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheGate = new();

    public static string? FindPackageLogoPath(string? shellPathOrAppId, string? displayName = null)
    {
        var appUserModelId = StoreAppIconResolver.ParseAppUserModelId(shellPathOrAppId)
            ?? (shellPathOrAppId?.Contains('!', StringComparison.Ordinal) == true ? shellPathOrAppId : null)
            ?? StartAppsCatalog.FindAppId(displayName ?? string.Empty);

        if (string.IsNullOrWhiteSpace(appUserModelId))
        {
            return null;
        }

        lock (CacheGate)
        {
            if (LogoPathCache.TryGetValue(appUserModelId, out var cached))
            {
                return cached;
            }
        }

        var logoPath = ResolveUncached(appUserModelId, displayName);

        lock (CacheGate)
        {
            LogoPathCache[appUserModelId] = logoPath;
        }

        return logoPath;
    }

    private static string? ResolveUncached(string appUserModelId, string? displayName)
    {
        var packageFamily = appUserModelId.Split('!')[0];
        var installLocation = GetPackageInstallLocation(packageFamily, displayName, appUserModelId);
        if (string.IsNullOrWhiteSpace(installLocation))
        {
            return null;
        }

        var assetsDirectory = Path.Combine(installLocation, "Assets");
        if (Directory.Exists(assetsDirectory))
        {
            var fromAssets = FindBestLogoInDirectory(assetsDirectory);
            if (!string.IsNullOrWhiteSpace(fromAssets))
            {
                return fromAssets;
            }
        }

        return FindBestLogoInDirectory(installLocation);
    }

    private static string? FindBestLogoInDirectory(string directory)
    {
        foreach (var fileName in LogoFileNames)
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        try
        {
            return Directory.EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly)
                .OrderByDescending(ScoreLogoPath)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int ScoreLogoPath(string path)
    {
        var file = Path.GetFileName(path);
        var score = 0;

        if (file.Contains("GraphicsLogo", StringComparison.OrdinalIgnoreCase))
        {
            score += 120;
        }

        if (file.Contains("44x44", StringComparison.OrdinalIgnoreCase))
        {
            score += 90;
        }

        if (file.Contains("StoreLogo", StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }

        if (file.Contains("SmallLogo", StringComparison.OrdinalIgnoreCase)
            && !file.Contains("altform", StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        if (file.Contains("Logo", StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        if (file.Contains("altform", StringComparison.OrdinalIgnoreCase))
        {
            score -= 25;
        }

        if (file.Contains("contrast", StringComparison.OrdinalIgnoreCase))
        {
            score -= 20;
        }

        if (file.Contains("targetsize", StringComparison.OrdinalIgnoreCase))
        {
            score -= 10;
        }

        return score;
    }

    private static string? GetPackageInstallLocation(
        string packageFamily,
        string? displayName,
        string appUserModelId)
    {
        lock (CacheGate)
        {
            if (InstallLocationCache.TryGetValue(packageFamily, out var cached))
            {
                return cached;
            }
        }

        var installLocation = QueryInstallLocation(packageFamily, displayName, appUserModelId);

        lock (CacheGate)
        {
            InstallLocationCache[packageFamily] = installLocation;
        }

        return installLocation;
    }

    private static string? QueryInstallLocation(
        string packageFamily,
        string? displayName,
        string appUserModelId)
    {
        var fromRegistry = ReadInstallLocationFromRegistry(packageFamily);
        if (!string.IsNullOrWhiteSpace(fromRegistry) && Directory.Exists(fromRegistry))
        {
            return fromRegistry;
        }

        var safeFamily = packageFamily.Replace("'", "''");
        var safeName = (displayName ?? string.Empty).Replace("'", "''");

        var script =
            "$family = '" + safeFamily + "'; " +
            "$name = '" + safeName + "'; " +
            "$pkg = Get-AppxPackage | Where-Object { $_.PackageFamilyName -eq $family } | Select-Object -First 1; " +
            "if (-not $pkg -and $name) { $pkg = Get-AppxPackage | Where-Object { $_.Name -like ('*' + $name + '*') -or $_.PackageFamilyName -like ('*' + $name + '*') } | Select-Object -First 1 }; " +
            "if (-not $pkg) { $pkg = Get-AppxPackage | Where-Object { $_.PackageFullName -like ($family + '*') } | Select-Object -First 1 }; " +
            "if ($pkg) { $pkg.InstallLocation }";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -STA -Command \"" + script + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return string.IsNullOrWhiteSpace(output) || !Directory.Exists(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadInstallLocationFromRegistry(string packageFamily)
    {
        var roots = new[]
        {
            $@"SOFTWARE\Classes\ActivatableClasses\Package\{packageFamily}",
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications"
        };

        foreach (var root in roots)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(root);
                if (key is null)
                {
                    continue;
                }

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    if (!subKeyName.StartsWith(packageFamily, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using var subKey = key.OpenSubKey(subKeyName);
                    var path = subKey?.GetValue("Path") as string;
                    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
                // Ignore registry access issues.
            }
        }

        return null;
    }
}
