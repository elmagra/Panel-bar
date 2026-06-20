using System.Diagnostics;
using System.Text.Json;

namespace PanelTray.Services;

public static class StartAppsCatalog
{
    private static Dictionary<string, string>? _appsByName;
    private static readonly object Gate = new();

    public static string? ResolveAppUserModelId(string displayName, string? executablePath, string? iconPath = null)
    {
        var fromPath = StoreAppIconResolver.ParseAppUserModelId(iconPath)
            ?? StoreAppIconResolver.ParseAppUserModelId(executablePath);
        if (!string.IsNullOrWhiteSpace(fromPath))
        {
            return fromPath;
        }

        return FindAppId(displayName);
    }

    public static string? FindAppId(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        EnsureLoaded();
        if (_appsByName is null || _appsByName.Count == 0)
        {
            return null;
        }

        if (_appsByName.TryGetValue(displayName.Trim(), out var exact))
        {
            return exact;
        }

        var normalized = NormalizeName(displayName);
        foreach (var (name, appId) in _appsByName)
        {
            if (NormalizeName(name) == normalized)
            {
                return appId;
            }
        }

        foreach (var (name, appId) in _appsByName)
        {
            if (name.Contains(displayName, StringComparison.OrdinalIgnoreCase)
                || displayName.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                return appId;
            }
        }

        return null;
    }

    private static void EnsureLoaded()
    {
        if (_appsByName is not null)
        {
            return;
        }

        lock (Gate)
        {
            if (_appsByName is not null)
            {
                return;
            }

            _appsByName = LoadStartApps();
        }
    }

    private static Dictionary<string, string> LoadStartApps()
    {
        var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -STA -Command \"[Console]::OutputEncoding=[System.Text.UTF8Encoding]::UTF8; Get-StartApps | ConvertTo-Json -Compress\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return apps;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            foreach (var (name, appId) in ParseStartAppsJson(output))
            {
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appId))
                {
                    continue;
                }

                if (!appId.Contains('!', StringComparison.Ordinal))
                {
                    continue;
                }

                apps.TryAdd(name.Trim(), appId.Trim());
            }
        }
        catch
        {
            // Ignore catalog load failures.
        }

        return apps;
    }

    private static IEnumerable<(string Name, string AppId)> ParseStartAppsJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<(string, string)>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return document.RootElement.EnumerateArray()
                    .Select(ReadStartApp)
                    .Where(item => item is not null)
                    .Select(item => item!.Value)
                    .ToArray();
            }

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                var item = ReadStartApp(document.RootElement);
                return item is null ? Array.Empty<(string, string)>() : [item.Value];
            }
        }
        catch (JsonException)
        {
            return Array.Empty<(string, string)>();
        }

        return Array.Empty<(string, string)>();
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

    private static string NormalizeName(string value)
        => value.Replace("®", string.Empty, StringComparison.Ordinal)
            .Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
}
