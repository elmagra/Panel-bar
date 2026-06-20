namespace PanelTray.Models;

public enum InstalledAppSource
{
    Shortcut,
    MicrosoftStore
}

public sealed class InstalledAppCandidate
{
    public string DisplayName { get; init; } = string.Empty;
    public string LaunchPath { get; init; } = string.Empty;
    public string? ShortcutPath { get; init; }
    public string? AppUserModelId { get; init; }
    public InstalledAppSource Source { get; init; }

    public string SourceLabel => Source switch
    {
        InstalledAppSource.Shortcut => "Acceso directo",
        InstalledAppSource.MicrosoftStore => "Microsoft Store",
        _ => "Aplicacion"
    };

    public string Detail => Source switch
    {
        InstalledAppSource.Shortcut => ShortcutPath ?? LaunchPath,
        InstalledAppSource.MicrosoftStore => AppUserModelId ?? LaunchPath,
        _ => LaunchPath
    };
}
