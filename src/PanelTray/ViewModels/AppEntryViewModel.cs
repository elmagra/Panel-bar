using System.Windows.Media;
using PanelTray.Models;
using PanelTray.Services;

namespace PanelTray.ViewModels;

public sealed class AppEntryViewModel : ViewModelBase
{
    private static readonly SolidColorBrush BackgroundStatusBrush = CreateFrozenBrush(250, 204, 21);

    private readonly AppEntry _model;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Action _changeCallback;
    private readonly IIconService _iconService;
    private AppRunState _runState = AppRunState.Closed;
    private ImageSource? _icon;
    private bool _isDragPlaceholder;
    private readonly bool _isDropSlot;

    public AppEntryViewModel(
        AppEntry model,
        Func<AppSettings> settingsProvider,
        Action changeCallback,
        IIconService iconService,
        bool isDropSlot = false)
    {
        _model = model;
        _settingsProvider = settingsProvider;
        _changeCallback = changeCallback;
        _iconService = iconService;
        _isDropSlot = isDropSlot;
        if (!_isDropSlot)
        {
            RefreshIcon();
        }
    }

    public static AppEntryViewModel CreateDropSlot(Func<AppSettings> settingsProvider)
        => new(
            new AppEntry(),
            settingsProvider,
            static () => { },
            DropSlotIconService.Instance,
            isDropSlot: true);

    public bool IsDropSlot => _isDropSlot;
    public TrayIntegrationKind TrayIntegration => _model.TrayIntegration;
    public bool UsesTrayFlyout => _model.TrayIntegration == TrayIntegrationKind.Tailscale;

    public AppEntry Model => _model;
    public Guid Id => _model.Id;

    public string DisplayName
    {
        get => _model.DisplayName;
        set
        {
            if (_model.DisplayName == value)
            {
                return;
            }

            _model.DisplayName = value;
            NotifyChanged();
        }
    }

    public string ExecutablePath
    {
        get => _model.ExecutablePath;
        set
        {
            if (_model.ExecutablePath == value)
            {
                return;
            }

            _model.ExecutablePath = value;
            RefreshIcon();
            NotifyChanged();
            OnPropertyChanged(nameof(PathStatus));
        }
    }

    public string Arguments
    {
        get => _model.Arguments;
        set
        {
            if (_model.Arguments == value)
            {
                return;
            }

            _model.Arguments = value;
            NotifyChanged();
        }
    }

    public string ProcessName
    {
        get => _model.ProcessName;
        set
        {
            var normalized = value?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true ? value[..^4] : value;
            if (_model.ProcessName == normalized)
            {
                return;
            }

            _model.ProcessName = normalized ?? string.Empty;
            NotifyChanged();
        }
    }

    public string SecondaryProcessNames
    {
        get => _model.SecondaryProcessNames;
        set
        {
            if (_model.SecondaryProcessNames == value)
            {
                return;
            }

            _model.SecondaryProcessNames = value ?? string.Empty;
            NotifyChanged();
        }
    }

    public string IconPath
    {
        get => _model.IconPath;
        set
        {
            if (_model.IconPath == value)
            {
                return;
            }

            _model.IconPath = value;
            RefreshIcon();
            NotifyChanged();
        }
    }

    public AppCategory Category
    {
        get => _model.Category;
        set
        {
            if (_model.Category == value)
            {
                return;
            }

            _model.Category = value;
            NotifyChanged();
        }
    }

    public int Order
    {
        get => _model.Order;
        set
        {
            if (_model.Order == value)
            {
                return;
            }

            _model.Order = value;
            NotifyChanged();
        }
    }

    public bool Enabled
    {
        get => _model.Enabled;
        set
        {
            if (_model.Enabled == value)
            {
                return;
            }

            _model.Enabled = value;
            NotifyChanged();
        }
    }

    public AppRunState RunState
    {
        get => _runState;
        set
        {
            if (SetProperty(ref _runState, value))
            {
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(StatusToolTip));
            }
        }
    }

    public bool IsRunning => RunState != AppRunState.Closed;

    public bool ShowName => _isDropSlot ? false : _model.ShowNameOverride ?? _settingsProvider().ShowNames;
    public int IconSize => _settingsProvider().IconSize;
    public int CardWidth => IconSize + 28;
    public bool IsEditMode => _isDropSlot ? false : _settingsProvider().EditMode;
    public bool IsDragPlaceholder
    {
        get => _isDragPlaceholder;
        set => SetProperty(ref _isDragPlaceholder, value);
    }
    public ImageSource? Icon => _icon;
    public System.Windows.Media.Brush StatusBrush => RunState switch
    {
        AppRunState.Active => System.Windows.Media.Brushes.LimeGreen,
        AppRunState.Background => BackgroundStatusBrush,
        _ => System.Windows.Media.Brushes.IndianRed
    };

    public string StatusToolTip => RunState switch
    {
        AppRunState.Active => "App abierta",
        AppRunState.Background => "Proceso en segundo plano",
        _ => "Cerrada"
    };
    public string PathStatus => IsLaunchPathValid() ? "Ruta valida" : "Ruta no encontrada o pendiente de configurar";

    public void RefreshFromSettings()
    {
        OnPropertyChanged(nameof(ShowName));
        OnPropertyChanged(nameof(IconSize));
        OnPropertyChanged(nameof(CardWidth));
        OnPropertyChanged(nameof(IsEditMode));
        RefreshIcon();
    }

    public void NotifyEditModeChanged() => OnPropertyChanged(nameof(IsEditMode));

    internal void SetReorderState(AppCategory category, int order)
    {
        _model.Category = category;
        _model.Order = order;
    }

    public void RefreshIcon()
    {
        _icon = _iconService.GetIcon(_model, IconSize);
        OnPropertyChanged(nameof(Icon));
    }

    public void RefreshProcessBindings()
    {
        OnPropertyChanged(nameof(ProcessName));
        OnPropertyChanged(nameof(SecondaryProcessNames));
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(string.Empty);
        _changeCallback();
    }

    private bool IsLaunchPathValid() => LaunchPathHelper.IsSupported(_model.ExecutablePath);

    private static SolidColorBrush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
