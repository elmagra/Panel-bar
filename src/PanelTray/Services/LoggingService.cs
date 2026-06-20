using System.Globalization;

namespace PanelTray.Services;

public sealed class LoggingService : ILoggingService
{
    private readonly object _gate = new();
    private readonly string _logFilePath;

    public LoggingService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiPanelTray",
            "logs");

        Directory.CreateDirectory(root);
        _logFilePath = Path.Combine(root, $"paneltray-{DateTime.Now:yyyyMMdd}.log");
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(Exception exception, string message)
        => Write("ERROR", $"{message}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        var line = string.Format(
            CultureInfo.InvariantCulture,
            "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}{3}",
            DateTime.Now,
            level,
            message,
            Environment.NewLine);

        lock (_gate)
        {
            File.AppendAllText(_logFilePath, line);
        }
    }
}
