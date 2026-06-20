namespace PanelTray.Services;

public interface ILoggingService
{
    void Info(string message);
    void Warn(string message);
    void Error(Exception exception, string message);
}
