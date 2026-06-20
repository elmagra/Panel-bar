using System.Text.Json;
using System.Text.Json.Serialization;
using PanelTray.Models;

namespace PanelTray.Services;

public sealed class ConfigService : IConfigService, IDisposable
{
    private static readonly HashSet<string> DefaultAppNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Discord",
        "Spotify",
        "Steam",
        "ChatGPT",
        "Google Drive",
        "WhatsApp",
        "Valorant",
        "Minecraft",
        "Epic Games",
        "NordVPN",
        "Bitdefender",
        "Tailscale"
    };

    private readonly ILoggingService _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _saveGate = new();
    private System.Threading.Timer? _saveTimer;

    public ConfigService(ILoggingService logger)
    {
        _logger = logger;
        ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiPanelTray");
        ConfigPath = Path.Combine(ConfigDirectory, "config.json");
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        Current = new RootConfig();
    }

    public string ConfigDirectory { get; }
    public string ConfigPath { get; }
    public RootConfig Current { get; private set; }

    public RootConfig Load()
    {
        Directory.CreateDirectory(ConfigDirectory);

        if (!File.Exists(ConfigPath))
        {
            Current = CreateDefaultConfig();
            Save();
            return Current;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            Current = JsonSerializer.Deserialize<RootConfig>(json, _jsonOptions) ?? CreateDefaultConfig();
            Normalize(Current);
            Save();
            return Current;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.Error(ex, "Could not load configuration; backing up corrupt file and regenerating defaults.");
            BackupCorruptConfig();
            Current = CreateDefaultConfig();
            Save();
            return Current;
        }
    }

    public void Save()
    {
        lock (_saveGate)
        {
            Directory.CreateDirectory(ConfigDirectory);
            Normalize(Current);

            var tempPath = Path.Combine(ConfigDirectory, "config.tmp");
            var json = JsonSerializer.Serialize(Current, _jsonOptions);
            File.WriteAllText(tempPath, json);

            if (File.Exists(ConfigPath))
            {
                File.Replace(tempPath, ConfigPath, null);
            }
            else
            {
                File.Move(tempPath, ConfigPath);
            }
        }
    }

    public void SaveDebounced()
    {
        _saveTimer?.Dispose();
        _saveTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                Save();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save configuration.");
            }
        }, null, 500, Timeout.Infinite);
    }

    public void FlushSave()
    {
        _saveTimer?.Dispose();
        _saveTimer = null;
        Save();
    }

    public void Dispose() => _saveTimer?.Dispose();

    private void BackupCorruptConfig()
    {
        if (!File.Exists(ConfigPath))
        {
            return;
        }

        var backupPath = Path.Combine(
            ConfigDirectory,
            $"config.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Copy(ConfigPath, backupPath, overwrite: true);
    }

    private static void Normalize(RootConfig config)
    {
        var previousSchemaVersion = config.SchemaVersion;
        config.Settings ??= new AppSettings();
        config.Apps ??= new List<AppEntry>();

        if (previousSchemaVersion < 2)
        {
            config.Settings.HideOnFocusLost = false;
        }

        if (previousSchemaVersion < 3)
        {
            EnsureApps(config, CreateSecurityApps());
        }

        if (previousSchemaVersion < 4)
        {
            MigrateTrayIntegrations(config);
        }

        if (previousSchemaVersion < 5)
        {
            MigrateShortcutIconPaths(config);
        }

        config.SchemaVersion = Math.Max(5, config.SchemaVersion);

        foreach (var app in config.Apps)
        {
            app.IconPath = ResolveAppIconPath(app.IconPath, app.ExecutablePath);
            app.IconPath = RefreshModernAppIconPath(app);

            if (string.IsNullOrWhiteSpace(app.SecondaryProcessNames))
            {
                ProcessInferenceService.Apply(app);
            }

            if (IsInvalidLearnedSecondary(app))
            {
                app.SecondaryProcessNames = string.Empty;
            }
        }

        if (!HotkeyParser.IsValid(config.Settings.HotkeyText))
        {
            config.Settings.HotkeyText = HotkeyParser.DefaultHotkey;
        }

        if (config.Settings.IconSize <= 0)
        {
            config.Settings.IconSize = 36;
        }

        var grouped = config.Apps
            .OrderBy(app => app.Category)
            .ThenBy(app => app.Order)
            .ThenBy(app => app.DisplayName)
            .GroupBy(app => app.Category);

        foreach (var group in grouped)
        {
            var index = 0;
            foreach (var app in group)
            {
                if (app.Id == Guid.Empty)
                {
                    app.Id = Guid.NewGuid();
                }

                app.DisplayName = string.IsNullOrWhiteSpace(app.DisplayName) ? "Nueva app" : app.DisplayName.Trim();
                app.ProcessName = CleanProcessName(app.ProcessName);
                MigrateLegacyApps(app);
                ResolveMissingDefaultApp(app);
                app.Order = index++;
            }
        }
    }

    private static void MigrateLegacyApps(AppEntry app)
    {
        if (!app.DisplayName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var usesWebEntry = app.ExecutablePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            || app.ProcessName.Equals("msedge", StringComparison.OrdinalIgnoreCase)
            || app.ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase);

        if (!usesWebEntry)
        {
            return;
        }

        var known = ShortcutResolver.FindKnownExecutable("ChatGPT")
            ?? ShortcutResolver.FindShortcut("ChatGPT");

        app.ExecutablePath = !string.IsNullOrWhiteSpace(known)
            ? known.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                ? ShortcutResolver.ResolveShortcutTarget(known) ?? known
                : known
            : @"%LOCALAPPDATA%\Microsoft\WindowsApps\ChatGPT.exe";
        app.ProcessName = "ChatGPT";
        app.Enabled = true;
    }

    private static void ResolveMissingDefaultApp(AppEntry app)
    {
        if (!DefaultAppNames.Contains(app.DisplayName))
        {
            return;
        }

        if (IsLaunchTargetUsable(app.ExecutablePath))
        {
            return;
        }

        var known = ShortcutResolver.FindKnownExecutable(app.DisplayName);
        if (!string.IsNullOrWhiteSpace(known))
        {
            app.ExecutablePath = known;
            app.Enabled = true;
            return;
        }

        var shortcut = ShortcutResolver.FindShortcut(app.DisplayName);
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            app.ExecutablePath = shortcut;
            app.Enabled = true;
            return;
        }

        // Keep examples editable in Settings, but do not show dead shortcuts in the main panel.
        app.Enabled = false;
    }

    private static bool IsLaunchTargetUsable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        return LaunchPathHelper.IsSupported(executablePath);
    }

    private static void MigrateShortcutIconPaths(RootConfig config)
    {
        foreach (var app in config.Apps)
        {
            app.IconPath = ResolveAppIconPath(app.IconPath, app.ExecutablePath);
        }
    }

    private static string RefreshModernAppIconPath(AppEntry app)
    {
        var appId = StartAppsCatalog.ResolveAppUserModelId(app.DisplayName, app.ExecutablePath, app.IconPath);

        if (string.IsNullOrWhiteSpace(appId)
            && !StoreAppIconResolver.IsShellIconPath(app.IconPath)
            && !StoreAppIconResolver.IsShellIconPath(app.ExecutablePath)
            && !IsProtocolLaunch(app.ExecutablePath))
        {
            return app.IconPath;
        }

        var packageLogo = PackageIconResolver.FindPackageLogoPath(appId ?? app.ExecutablePath, app.DisplayName);
        if (!string.IsNullOrWhiteSpace(packageLogo))
        {
            return packageLogo;
        }

        if (!string.IsNullOrWhiteSpace(app.IconPath) && !StoreAppIconResolver.IsShellIconPath(app.IconPath))
        {
            return app.IconPath;
        }

        return StoreAppIconResolver.ResolveIconPath(app.DisplayName, app.ExecutablePath, appId);
    }

    private static bool IsProtocolLaunch(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var expanded = Environment.ExpandEnvironmentVariables(executablePath.Trim());
        return expanded.EndsWith(":", StringComparison.Ordinal)
            || (Uri.TryCreate(expanded, UriKind.Absolute, out var uri)
                && uri.Scheme is not ("http" or "https" or "file"));
    }

    private static bool IsInvalidLearnedSecondary(AppEntry app)
    {
        if (string.IsNullOrWhiteSpace(app.SecondaryProcessNames))
        {
            return false;
        }

        var secondary = app.SecondaryProcessNames.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return secondary.All(IsImplausibleSecondaryProcess);
    }

    private static bool IsImplausibleSecondaryProcess(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        if (name.Contains(':'))
        {
            return true;
        }

        if (char.IsDigit(name[0]))
        {
            return true;
        }

        if (name.Equals("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return name.Count(ch => ch == '.') >= 2;
    }

    private static string ResolveAppIconPath(string iconPath, string executablePath)
    {
        if (iconPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return ShortcutResolver.ResolveShortcutIconPath(iconPath) ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            return iconPath;
        }

        if (executablePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return ShortcutResolver.ResolveShortcutIconPath(executablePath) ?? string.Empty;
        }

        return string.Empty;
    }

    private static void MigrateTrayIntegrations(RootConfig config)
    {
        foreach (var app in config.Apps)
        {
            if (app.DisplayName.Equals("Tailscale", StringComparison.OrdinalIgnoreCase))
            {
                app.TrayIntegration = TrayIntegrationKind.Tailscale;
            }
        }
    }

    private static void EnsureApps(RootConfig config, IEnumerable<AppEntry> apps)
    {
        var existing = new HashSet<string>(
            config.Apps.Select(app => app.DisplayName),
            StringComparer.OrdinalIgnoreCase);

        foreach (var app in apps)
        {
            if (existing.Contains(app.DisplayName))
            {
                continue;
            }

            config.Apps.Add(app);
        }
    }

    private static IEnumerable<AppEntry> CreateSecurityApps()
    {
        yield return App("NordVPN", @"%ProgramFiles%\NordVPN\NordVPN.exe", "", "NordVPN", AppCategory.Apps, 5, "nordvpn-service");
        yield return App("Bitdefender", @"%ProgramFiles%\Bitdefender\Bitdefender Security App\seccenter.exe", "", "seccenter", AppCategory.Apps, 6, "bdagent");
        yield return CreateTailscaleApp();
    }

    private static AppEntry CreateTailscaleApp()
    {
        var app = App("Tailscale", @"%ProgramFiles%\Tailscale\tailscale-ipn.exe", "", "tailscale-ipn", AppCategory.Apps, 7, "tailscale");
        app.TrayIntegration = TrayIntegrationKind.Tailscale;
        return app;
    }

    private static IEnumerable<AppEntry> CreateBundledApps()
    {
        yield return App("Discord", @"%LOCALAPPDATA%\Discord\Update.exe", "--processStart Discord.exe", "Discord", AppCategory.Apps, 0);
        yield return App("Spotify", @"%APPDATA%\Spotify\Spotify.exe", "", "Spotify", AppCategory.Apps, 1);
        yield return App("Steam", @"C:\Program Files (x86)\Steam\steam.exe", "", "steam", AppCategory.Juegos, 0);
        yield return App("ChatGPT", @"%LOCALAPPDATA%\Microsoft\WindowsApps\ChatGPT.exe", "", "ChatGPT", AppCategory.Apps, 2);
        yield return App("Google Drive", @"C:\Program Files\Google\Drive File Stream\GoogleDriveFS.exe", "", "GoogleDriveFS", AppCategory.Apps, 3);
        yield return App("WhatsApp", @"%LOCALAPPDATA%\WhatsApp\WhatsApp.exe", "", "WhatsApp", AppCategory.Apps, 4);
        foreach (var app in CreateSecurityApps())
        {
            yield return app;
        }

        yield return App("Valorant", @"C:\Riot Games\Riot Client\RiotClientServices.exe", "--launch-product=valorant --launch-patchline=live", "VALORANT-Win64-Shipping", AppCategory.Juegos, 1, "RiotClientServices");
        yield return App("Minecraft", "minecraft:", "", "Minecraft.Windows", AppCategory.Juegos, 2);
        yield return App("Epic Games", @"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win32\EpicGamesLauncher.exe", "", "EpicGamesLauncher", AppCategory.Juegos, 3, "EpicWebHelper");
    }

    private static RootConfig CreateDefaultConfig()
    {
        var config = new RootConfig
        {
            SchemaVersion = 5,
            Settings = new AppSettings()
        };

        foreach (var app in CreateBundledApps())
        {
            config.Apps.Add(app);
        }

        return config;
    }

    private static AppEntry App(
        string name,
        string path,
        string args,
        string process,
        AppCategory category,
        int order,
        string secondaryProcesses = "")
    {
        return new AppEntry
        {
            DisplayName = name,
            ExecutablePath = path,
            Arguments = args,
            ProcessName = CleanProcessName(process),
            SecondaryProcessNames = secondaryProcesses,
            Category = category,
            Order = order,
            Enabled = true
        };
    }

    private static string CleanProcessName(string processName)
    {
        processName = (processName ?? string.Empty).Trim();
        return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
    }
}
