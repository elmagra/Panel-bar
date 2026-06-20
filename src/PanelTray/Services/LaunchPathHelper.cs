namespace PanelTray.Services;

public static class LaunchPathHelper
{
    public static bool IsSupported(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var expanded = Environment.ExpandEnvironmentVariables(executablePath.Trim());
        if (expanded.EndsWith(':'))
        {
            return true;
        }

        if (Uri.TryCreate(expanded, UriKind.Absolute, out var uri))
        {
            return uri.Scheme is "http" or "https" or "shell";
        }

        return File.Exists(expanded) || Directory.Exists(expanded);
    }
}
