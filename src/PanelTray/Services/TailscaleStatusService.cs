using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PanelTray.Services;

public sealed class TailscaleStatusSnapshot
{
    public bool IsConnected { get; init; }
    public string StateText { get; init; } = string.Empty;
    public string HostName { get; init; } = string.Empty;
    public string PrimaryIp { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public IReadOnlyList<string> PeerNames { get; init; } = Array.Empty<string>();
}

public sealed class TailscaleStatusService
{
    private static readonly string[] ExecutableCandidates =
    [
        @"%ProgramFiles%\Tailscale\tailscale.exe",
        @"C:\Program Files\Tailscale\tailscale.exe"
    ];

    private readonly ILoggingService _logger;

    public TailscaleStatusService(ILoggingService logger)
    {
        _logger = logger;
    }

    public TailscaleStatusSnapshot? TryRead()
    {
        var executable = ResolveExecutable();
        if (executable is null)
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "status --json",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var json = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var payload = JsonSerializer.Deserialize<TailscaleStatusPayload>(json);
            if (payload is null)
            {
                return null;
            }

            var self = payload.Self;
            var primaryIp = self?.TailscaleIPs?.FirstOrDefault(ip => ip.Contains('.', StringComparison.Ordinal))
                ?? payload.TailscaleIPs?.FirstOrDefault(ip => ip.Contains('.', StringComparison.Ordinal))
                ?? string.Empty;

            var connected = string.Equals(payload.BackendState, "Running", StringComparison.OrdinalIgnoreCase);
            var peers = payload.Peer?
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.HostName))
                .Select(pair => pair.Value.HostName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToArray()
                ?? Array.Empty<string>();

            return new TailscaleStatusSnapshot
            {
                IsConnected = connected,
                StateText = connected ? "Connected" : payload.BackendState ?? "Unknown",
                HostName = self?.HostName ?? Environment.MachineName,
                PrimaryIp = primaryIp,
                Version = payload.Version ?? string.Empty,
                AccountName = payload.UserProfile?.LoginName ?? string.Empty,
                PeerNames = peers
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not read Tailscale status.");
            return null;
        }
    }

    private static string? ResolveExecutable()
    {
        foreach (var candidate in ExecutableCandidates)
        {
            var expanded = Environment.ExpandEnvironmentVariables(candidate);
            if (File.Exists(expanded))
            {
                return expanded;
            }
        }

        return null;
    }

    private sealed class TailscaleStatusPayload
    {
        public string? Version { get; set; }
        public string? BackendState { get; set; }
        public string[]? TailscaleIPs { get; set; }
        public TailscaleSelf? Self { get; set; }
        public TailscaleUserProfile? UserProfile { get; set; }
        public Dictionary<string, TailscalePeer>? Peer { get; set; }
    }

    private sealed class TailscaleSelf
    {
        public string? HostName { get; set; }
        public string[]? TailscaleIPs { get; set; }
    }

    private sealed class TailscaleUserProfile
    {
        [JsonPropertyName("LoginName")]
        public string? LoginName { get; set; }
    }

    private sealed class TailscalePeer
    {
        public string? HostName { get; set; }
    }
}
