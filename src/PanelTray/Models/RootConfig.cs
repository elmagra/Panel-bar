namespace PanelTray.Models;

public sealed class RootConfig
{
    public int SchemaVersion { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public List<AppEntry> Apps { get; set; } = new();
}
