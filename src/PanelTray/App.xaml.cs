using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Interop;
using PanelTray.Services;
using PanelTray.ViewModels;
using PanelTray.Views;
using Application = System.Windows.Application;

namespace PanelTray;

public partial class App : Application
{
    private const string MutexName = "Local\\MiPanelTray.SingleInstance";
    private const string ShowEventName = "Local\\MiPanelTray.ShowPanel";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _showEventCancellation;
    private LoggingService? _logger;
    private ConfigService? _configService;
    private IProcessMonitorService? _processMonitor;
    private ITrayService? _trayService;
    private HotkeyService? _hotkeyService;
    private ThemeService? _themeService;
    private PanelWindow? _panelWindow;
    private SettingsWindow? _settingsWindow;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        _ownsMutex = createdNew;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        if (!createdNew)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        try
        {
            _logger = new LoggingService();
            _logger.Info("Starting PanelTray.");

            _configService = new ConfigService(_logger);
            _configService.Load();

            _themeService = new ThemeService();
            _themeService.Apply(_configService.Current.Settings.Theme);

            var processLearning = new ProcessLearningService(_logger, _configService);
            var launcher = new LauncherService(_logger, processLearning);
            var iconService = new IconService();
            var tailscaleStatusService = new TailscaleStatusService(_logger);
            var nativeTrayMenuService = new NativeTrayMenuService(_logger);
            var startup = new StartupService();
            _processMonitor = new ProcessMonitorService(_logger);
            _trayService = new TrayService();
            _hotkeyService = new HotkeyService(_logger);

            var panelViewModel = new PanelViewModel(
                _configService,
                launcher,
                _processMonitor,
                iconService,
                tailscaleStatusService,
                nativeTrayMenuService,
                processLearning);
            var settingsViewModel = new SettingsViewModel(panelViewModel, startup, _themeService);

            _panelWindow = new PanelWindow(panelViewModel);
            _settingsWindow = new SettingsWindow(settingsViewModel);
            new WindowInteropHelper(_panelWindow).EnsureHandle();

            panelViewModel.SettingsRequested += (_, _) => ShowSettings();
            panelViewModel.ExitRequested += (_, _) => ExitApplication();
            panelViewModel.SettingsChanged += (_, _) => RegisterHotkey();
            panelViewModel.EditRequested += (_, app) =>
            {
                settingsViewModel.SelectedApp = app;
                ShowSettings();
            };

            _trayService.TogglePanelRequested += (_, _) => TogglePanel();
            _trayService.SettingsRequested += (_, _) => ShowSettings();
            _trayService.RestartRequested += (_, _) => Restart();
            _trayService.ExitRequested += (_, _) => ExitApplication();
            _trayService.Initialize();

            _processMonitor.SetFastPolling(false);
            _processMonitor.Start();
            RegisterHotkey();

            StartShowEventLoop();
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Startup failed.");
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("Exiting PanelTray.");
        _configService?.FlushSave();
        _showEventCancellation?.Cancel();
        _hotkeyService?.Dispose();
        _processMonitor?.Dispose();
        _trayService?.Dispose();
        _showEvent?.Dispose();
        if (_ownsMutex)
        {
            _mutex?.ReleaseMutex();
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void TogglePanel()
    {
        if (_panelWindow is null || _processMonitor is null)
        {
            return;
        }

        if (_panelWindow.IsVisible)
        {
            _panelWindow.Hide();
            _processMonitor.SetFastPolling(false);
        }
        else
        {
            _panelWindow.ShowNearTray();
            _processMonitor.SetFastPolling(true);
        }
    }

    private void ShowPanel()
    {
        if (_panelWindow is null || _processMonitor is null)
        {
            return;
        }

        _panelWindow.ShowNearTray();
        _processMonitor.SetFastPolling(true);
    }

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            return;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void Restart()
    {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true
            });
        }

        ExitApplication();
    }

    private void ExitApplication()
    {
        if (_panelWindow is not null)
        {
            _panelWindow.AllowClose();
        }

        if (_settingsWindow is not null)
        {
            _settingsWindow.AllowClose();
        }

        Shutdown();
    }

    private void StartShowEventLoop()
    {
        if (_showEvent is null)
        {
            return;
        }

        _showEventCancellation = new CancellationTokenSource();
        var token = _showEventCancellation.Token;
        var handle = _showEvent;

        Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                var signaled = handle.WaitOne(1000);
                if (signaled && !token.IsCancellationRequested)
                {
                    Dispatcher.Invoke(ShowPanel);
                }
            }
        }, token);
    }

    private void RegisterHotkey()
    {
        if (_panelWindow is null || _configService is null || _hotkeyService is null)
        {
            return;
        }

        _hotkeyService.Register(
            _panelWindow,
            _configService.Current.Settings.HotkeyText,
            () => Dispatcher.Invoke(TogglePanel));
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error(e.Exception, "Unhandled dispatcher exception.");
        e.Handled = true;
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _logger?.Error(exception, "Unhandled application domain exception.");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.Error(e.Exception, "Unobserved task exception.");
        e.SetObserved();
    }
}
