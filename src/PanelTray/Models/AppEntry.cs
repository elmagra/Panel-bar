using System.Text.Json.Serialization;

namespace PanelTray.Models;

public sealed class AppEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string SecondaryProcessNames { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
    public AppCategory Category { get; set; } = AppCategory.Apps;
    public int Order { get; set; }
    public bool Enabled { get; set; } = true;
    public bool? ShowNameOverride { get; set; }
    public TrayIntegrationKind TrayIntegration { get; set; }

    [JsonIgnore]
    public bool HasLaunchPath => !string.IsNullOrWhiteSpace(ExecutablePath);
}
