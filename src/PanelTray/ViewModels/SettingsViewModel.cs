using System.Windows.Input;
using Microsoft.Win32;
using PanelTray.Models;
using PanelTray.Services;

namespace PanelTray.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly PanelViewModel _panelViewModel;
    private readonly IStartupService _startupService;
    private readonly ThemeService _themeService;

    public SettingsViewModel(PanelViewModel panelViewModel, IStartupService startupService, ThemeService themeService)
    {
        _panelViewModel = panelViewModel;
        _startupService = startupService;
        _themeService = themeService;

        BrowseExecutableCommand = new RelayCommand(BrowseExecutable);
        BrowseIconCommand = new RelayCommand(BrowseIcon);
        AddAppCommand = _panelViewModel.AddAppCommand;
        DeleteSelectedCommand = new RelayCommand(() => _panelViewModel.DeleteApp(SelectedApp), () => SelectedApp is not null);
        ResetOrderCommand = _panelViewModel.ResetOrderCommand;

        Settings.StartWithWindows = _startupService.IsEnabled();
    }

    public PanelViewModel Panel => _panelViewModel;
    public AppSettings Settings => _panelViewModel.Settings;
    public AppCategory[] Categories => _panelViewModel.Categories;
    public ThemeMode[] Themes { get; } = Enum.GetValues<ThemeMode>();

    public AppEntryViewModel? SelectedApp
    {
        get => _panelViewModel.SelectedApp;
        set
        {
            _panelViewModel.SelectedApp = value;
            OnPropertyChanged();
            (DeleteSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool ShowNames
    {
        get => Settings.ShowNames;
        set
        {
            if (Settings.ShowNames == value)
            {
                return;
            }

            Settings.ShowNames = value;
            ChangedSettings();
        }
    }

    public int IconSize
    {
        get => Settings.IconSize;
        set
        {
            var clamped = Math.Clamp(value, 32, 96);
            if (Settings.IconSize == clamped)
            {
                return;
            }

            Settings.IconSize = clamped;
            ChangedSettings();
        }
    }

    public ThemeMode Theme
    {
        get => Settings.Theme;
        set
        {
            if (Settings.Theme == value)
            {
                return;
            }

            Settings.Theme = value;
            _themeService.Apply(value);
            ChangedSettings();
        }
    }

    public bool StartWithWindows
    {
        get => Settings.StartWithWindows;
        set
        {
            if (Settings.StartWithWindows == value)
            {
                return;
            }

            Settings.StartWithWindows = value;
            _startupService.SetEnabled(value);
            ChangedSettings();
        }
    }

    public bool HideOnFocusLost
    {
        get => Settings.HideOnFocusLost;
        set
        {
            if (Settings.HideOnFocusLost == value)
            {
                return;
            }

            Settings.HideOnFocusLost = value;
            ChangedSettings();
        }
    }

    public string HotkeyText
    {
        get => Settings.HotkeyText;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "Ctrl+Alt+P" : value.Trim();
            if (Settings.HotkeyText == normalized)
            {
                return;
            }

            Settings.HotkeyText = normalized;
            ChangedSettings();
        }
    }

    public double PanelWidth
    {
        get => Settings.PanelWidth;
        set
        {
            var clamped = Math.Clamp(value, 320, 720);
            if (Math.Abs(Settings.PanelWidth - clamped) < 0.1)
            {
                return;
            }

            Settings.PanelWidth = clamped;
            ChangedSettings();
        }
    }

    public double PanelHeight
    {
        get => Settings.PanelHeight;
        set
        {
            var clamped = Math.Clamp(value, 360, 900);
            if (Math.Abs(Settings.PanelHeight - clamped) < 0.1)
            {
                return;
            }

            Settings.PanelHeight = clamped;
            ChangedSettings();
        }
    }

    public ICommand BrowseExecutableCommand { get; }
    public ICommand BrowseIconCommand { get; }
    public ICommand AddAppCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand ResetOrderCommand { get; }

    private void BrowseExecutable()
    {
        if (SelectedApp is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Ejecutables y accesos directos|*.exe;*.lnk|Todos los archivos|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedApp.ExecutablePath = dialog.FileName;
            SelectedApp.ProcessName = Path.GetFileNameWithoutExtension(dialog.FileName);
            _panelViewModel.FlushPendingSave();
        }
    }

    private void BrowseIcon()
    {
        if (SelectedApp is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Iconos e imagenes|*.ico;*.png;*.jpg;*.jpeg;*.exe|Todos los archivos|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedApp.IconPath = dialog.FileName;
            _panelViewModel.FlushPendingSave();
        }
    }

    public void FlushPendingChanges() => _panelViewModel.FlushPendingSave();

    private void ChangedSettings()
    {
        OnPropertyChanged(string.Empty);
        _panelViewModel.RefreshSettings();
    }
}
