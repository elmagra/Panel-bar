using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using PanelTray.Models;
using PanelTray.Services;
using PanelTray.Views;

namespace PanelTray.ViewModels;

public sealed class PanelViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly ILauncherService _launcherService;
    private readonly IProcessMonitorService _processMonitorService;
    private readonly IIconService _iconService;
    private readonly TailscaleStatusService _tailscaleStatusService;
    private readonly NativeTrayMenuService _nativeTrayMenuService;
    private readonly InstalledAppDiscoveryService _installedAppDiscoveryService = new();
    private readonly AppEntryViewModel _dropSlot;
    private readonly ObservableCollection<AppEntryViewModel> _displayApps = new();
    private AppCategory _selectedCategory = AppCategory.Apps;
    private AppEntryViewModel? _selectedApp;
    private Dictionary<Guid, (AppCategory Category, int Order)>? _dragSnapshot;
    private string? _lastPreviewSignature;
    private bool _isDragSessionActive;
    private Guid? _draggingAppId;
    private int? _hoverInsertIndex;
    private AppCategory? _previewCategory;

    public PanelViewModel(
        IConfigService configService,
        ILauncherService launcherService,
        IProcessMonitorService processMonitorService,
        IIconService iconService,
        TailscaleStatusService tailscaleStatusService,
        NativeTrayMenuService nativeTrayMenuService,
        IProcessLearningService? processLearningService = null)
    {
        _configService = configService;
        _launcherService = launcherService;
        _processMonitorService = processMonitorService;
        _iconService = iconService;
        _tailscaleStatusService = tailscaleStatusService;
        _nativeTrayMenuService = nativeTrayMenuService;
        _dropSlot = AppEntryViewModel.CreateDropSlot(() => Settings);

        Apps = new ObservableCollection<AppEntryViewModel>(
            _configService.Current.Apps
                .OrderBy(app => app.Category)
                .ThenBy(app => app.Order)
                .Select(CreateAppViewModel));

        Categories = Enum.GetValues<AppCategory>();

        OpenOrActivateCommand = new RelayCommand(arg => RunFor(arg, _launcherService.OpenOrActivate));
        OpenCommand = new RelayCommand(arg => RunFor(arg, _launcherService.Open));
        CloseCommand = new RelayCommand(arg => RunFor(arg, _launcherService.Close));
        CloseAllCommand = new RelayCommand(arg => RunFor(arg, _launcherService.CloseAll));
        RestartCommand = new RelayCommand(arg => RunFor(arg, _launcherService.Restart));
        OpenLocationCommand = new RelayCommand(arg => RunFor(arg, _launcherService.OpenLocation));
        DeleteCommand = new RelayCommand(arg => DeleteApp(arg as AppEntryViewModel));
        ResetOrderCommand = new RelayCommand(ResetOrder);
        AddAppCommand = new RelayCommand(AddApp);
        ToggleEditModeCommand = new RelayCommand(() => EditMode = !EditMode);

        _processMonitorService.SetApps(() => Apps.Select(app => app.Model).ToArray());
        if (processLearningService is not null)
        {
            processLearningService.ProcessesLearned += (_, app) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var viewModel = Apps.FirstOrDefault(entry => entry.Id == app.Id);
                    viewModel?.RefreshProcessBindings();
                    _processMonitorService.RefreshNow();
                });
            };
        }

        _processMonitorService.StatusChanged += (_, states) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var app in Apps)
                {
                    if (states.TryGetValue(app.Id, out var state))
                    {
                        app.RunState = state;
                    }
                }
            });
        };

        RebuildDisplayApps();
    }

    public ObservableCollection<AppEntryViewModel> Apps { get; }
    public AppCategory[] Categories { get; }
    public AppSettings Settings => _configService.Current.Settings;

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
            OnPropertyChanged();
            RefreshSettings();
        }
    }

    public IEnumerable<AppEntryViewModel> VisibleApps => Apps
        .Where(app => app.Category == SelectedCategory && app.Enabled && !app.IsDragPlaceholder)
        .OrderBy(app => app.Order);

    public ObservableCollection<AppEntryViewModel> DisplayApps => _displayApps;

    public AppCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                if (_isDragSessionActive)
                {
                    _previewCategory = value;
                    _lastPreviewSignature = null;
                }

                RefreshDisplayApps();
            }
        }
    }

    public AppEntryViewModel? SelectedApp
    {
        get => _selectedApp;
        set => SetProperty(ref _selectedApp, value);
    }

    public bool EditMode
    {
        get => Settings.EditMode;
        set
        {
            if (Settings.EditMode == value)
            {
                return;
            }

            Settings.EditMode = value;
            OnPropertyChanged();
            NotifyEditModeChanged();
            RequestSave();
        }
    }

    public ICommand OpenOrActivateCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand CloseAllCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand OpenLocationCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ResetOrderCommand { get; }
    public ICommand AddAppCommand { get; }
    public ICommand ToggleEditModeCommand { get; }

    public event EventHandler<AppEntryViewModel?>? EditRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? SettingsChanged;

    public void RequestSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    public void RequestEdit(AppEntryViewModel? app) => EditRequested?.Invoke(this, app);

    public void RequestExit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    public void RequestSave()
    {
        Reindex();
        _configService.SaveDebounced();
        RefreshDisplayApps();
    }

    public void FlushPendingSave()
    {
        Reindex();
        _configService.FlushSave();
        RefreshDisplayApps();
    }

    public void AddApp()
    {
        var order = Apps.Where(app => app.Category == SelectedCategory).Select(app => app.Order).DefaultIfEmpty(-1).Max() + 1;
        var entry = new AppEntry
        {
            DisplayName = "Nueva app",
            Category = SelectedCategory,
            Order = order
        };

        _configService.Current.Apps.Add(entry);
        var viewModel = CreateAppViewModel(entry);
        Apps.Add(viewModel);
        SelectedApp = viewModel;
        RequestSave();
        RequestEdit(viewModel);
    }

    public void TryAddInstalledApp(Window owner)
    {
        var pickerViewModel = new InstalledAppPickerViewModel(
            _installedAppDiscoveryService,
            GetExistingLaunchKeys);

        var dialog = new InstalledAppPickerWindow(pickerViewModel)
        {
            Owner = owner
        };

        if (dialog.ShowDialog() == true && pickerViewModel.SelectedApp is not null)
        {
            AddAppFromInstalled(pickerViewModel.SelectedApp);
        }
    }

    public AppEntryViewModel? AddAppFromInstalled(InstalledAppCandidate candidate)
    {
        var entry = InstalledAppImportService.CreateAppEntry(candidate);
        if (entry is null)
        {
            return null;
        }

        entry.Category = SelectedCategory;
        entry.Order = Apps.Where(app => app.Category == SelectedCategory).Select(app => app.Order).DefaultIfEmpty(-1).Max() + 1;
        _configService.Current.Apps.Add(entry);
        var viewModel = CreateAppViewModel(entry);
        Apps.Add(viewModel);
        SelectedApp = viewModel;
        RequestSave();
        RefreshDisplayApps();
        return viewModel;
    }

    private IEnumerable<string> GetExistingLaunchKeys()
        => Apps.Select(app => InstalledAppDiscoveryService.NormalizeLaunchKey(app.ExecutablePath));

    public AppEntryViewModel? AddAppFromShortcut(
        string path,
        AppCategory category,
        AppEntryViewModel? target)
    {
        var entry = ShortcutImportService.TryCreateApp(path);
        if (entry is null)
        {
            return null;
        }

        entry.Category = category;
        _configService.Current.Apps.Add(entry);
        var viewModel = CreateAppViewModel(entry);
        Apps.Add(viewModel);

        var visible = GetVisibleAppsInCategory(category, viewModel.Id);
        var insertIndex = target is null || target.IsDropSlot || target == viewModel
            ? visible.Count
            : Math.Max(0, visible.IndexOf(target));

        ApplyInsertMove(viewModel, insertIndex, category);
        SelectedApp = viewModel;
        RequestSave();
        return viewModel;
    }

    public void DeleteApp(AppEntryViewModel? app)
    {
        if (app is null || app.IsDropSlot)
        {
            return;
        }

        Apps.Remove(app);
        _configService.Current.Apps.Remove(app.Model);
        RequestSave();
    }

    public void MoveApp(AppEntryViewModel source, AppEntryViewModel? target, AppCategory targetCategory)
    {
        if (source.IsDropSlot)
        {
            return;
        }

        var visible = GetVisibleAppsInCategory(targetCategory, source.Id);
        var insertIndex = target is null || target.IsDropSlot
            ? visible.Count
            : Math.Max(0, visible.IndexOf(target));
        ApplyInsertMove(source, insertIndex, targetCategory);
        RequestSave();
    }

    public void BeginDragSession(AppEntryViewModel source)
    {
        _dragSnapshot = Apps
            .Where(app => !app.IsDropSlot)
            .ToDictionary(app => app.Id, app => (app.Category, app.Order));
        _isDragSessionActive = true;
        _draggingAppId = source.Id;
        _previewCategory = SelectedCategory;
        _hoverInsertIndex = GetInsertIndexForOrder(source);
        _lastPreviewSignature = null;
        source.IsDragPlaceholder = true;
        RebuildDisplayApps();
    }

    public void PreviewMoveApp(
        AppEntryViewModel source,
        AppEntryViewModel? target,
        AppCategory targetCategory)
    {
        if (source.IsDropSlot || target?.IsDropSlot == true)
        {
            return;
        }

        _previewCategory = targetCategory;

        var visible = GetVisibleAppsInCategory(targetCategory, source.Id);
        var hoverIndex = target is null ? visible.Count : visible.IndexOf(target);
        if (target is not null && hoverIndex < 0)
        {
            return;
        }

        var signature = $"{targetCategory}|{hoverIndex}";
        if (_lastPreviewSignature == signature)
        {
            return;
        }

        _hoverInsertIndex = hoverIndex;
        _draggingAppId = source.Id;
        _lastPreviewSignature = signature;

        if (_displayApps.Contains(_dropSlot))
        {
            MoveDropSlotTo(hoverIndex);
        }
        else
        {
            RebuildDisplayApps();
        }
    }

    public void CommitDragSession()
    {
        AppEntryViewModel? source = null;
        var insertIndex = 0;

        if (_draggingAppId is Guid id && _hoverInsertIndex is int hoverIndex)
        {
            source = Apps.FirstOrDefault(app => app.Id == id);
            insertIndex = hoverIndex;
            if (source is not null)
            {
                ApplyInsertMove(source, hoverIndex, _previewCategory ?? source.Category);
            }
        }

        EndDragSession();

        RebuildDisplayApps();
        _configService.SaveDebounced();
    }

    public void CancelDragSession()
    {
        if (_dragSnapshot is not null)
        {
            foreach (var app in Apps.Where(app => !app.IsDropSlot))
            {
                if (_dragSnapshot.TryGetValue(app.Id, out var state))
                {
                    app.SetReorderState(state.Category, state.Order);
                }
            }
        }

        EndDragSession();
        RebuildDisplayApps();
    }

    public void RefreshDisplayApps()
    {
        OnPropertyChanged(nameof(VisibleApps));
        RebuildDisplayApps();
    }

    public void ResetOrder()
    {
        foreach (var group in Apps.Where(app => !app.IsDropSlot).GroupBy(app => app.Category))
        {
            var index = 0;
            foreach (var app in group.OrderBy(app => app.DisplayName))
            {
                app.Order = index++;
            }
        }

        RequestSave();
    }

    public void RefreshSettings()
    {
        foreach (var app in Apps.Where(app => !app.IsDropSlot))
        {
            app.RefreshFromSettings();
        }

        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(EditMode));
        RefreshDisplayApps();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        RequestSave();
    }

    private void NotifyEditModeChanged()
    {
        foreach (var app in Apps.Where(app => !app.IsDropSlot))
        {
            app.NotifyEditModeChanged();
        }
    }

    public void RefreshProcesses() => _processMonitorService.RefreshNow();

    public TailscaleStatusSnapshot? GetTailscaleStatus() => _tailscaleStatusService.TryRead();

    public bool TryShowNativeTrayMenu(AppEntryViewModel app)
        => !app.IsDropSlot && _nativeTrayMenuService.TryShowContextMenuForApp(app.Model);

    public void CloseTailscaleApp()
    {
        var app = Apps.FirstOrDefault(entry => entry.TrayIntegration == TrayIntegrationKind.Tailscale);
        if (app is not null)
        {
            _launcherService.CloseAll(app.Model);
        }
    }

    private AppEntryViewModel CreateAppViewModel(AppEntry entry)
        => new(entry, () => Settings, RequestSave, _iconService);

    private static void RunFor(object? parameter, Action<AppEntry> action)
    {
        if (parameter is AppEntryViewModel { IsDropSlot: false } app)
        {
            action(app.Model);
        }
    }

    private void Reindex()
    {
        foreach (var group in Apps.Where(app => !app.IsDropSlot).GroupBy(app => app.Category))
        {
            var index = 0;
            foreach (var app in group.OrderBy(app => app.Order).ThenBy(app => app.DisplayName))
            {
                app.Model.Order = index++;
            }
        }
    }

    private void EndDragSession()
    {
        foreach (var app in Apps.Where(app => !app.IsDropSlot))
        {
            app.IsDragPlaceholder = false;
        }

        _isDragSessionActive = false;
        _draggingAppId = null;
        _hoverInsertIndex = null;
        _previewCategory = null;
        _lastPreviewSignature = null;
        _dragSnapshot = null;
    }

    private void RebuildDisplayApps()
    {
        _displayApps.Clear();
        foreach (var app in ComputeDisplayList())
        {
            _displayApps.Add(app);
        }
    }

    private IEnumerable<AppEntryViewModel> ComputeDisplayList()
    {
        if (!_isDragSessionActive || _draggingAppId is null || _hoverInsertIndex is not int hoverIndex)
        {
            return VisibleApps;
        }

        var category = _previewCategory ?? SelectedCategory;
        var visible = GetVisibleAppsInCategory(category, _draggingAppId.Value);
        hoverIndex = Math.Clamp(hoverIndex, 0, visible.Count);

        var result = new List<AppEntryViewModel>(visible.Count + 1);
        for (var i = 0; i <= visible.Count; i++)
        {
            if (i == hoverIndex)
            {
                result.Add(_dropSlot);
            }

            if (i < visible.Count)
            {
                result.Add(visible[i]);
            }
        }

        return result;
    }

    private void MoveDropSlotTo(int hoverIndex)
    {
        var slotIndex = _displayApps.IndexOf(_dropSlot);
        if (slotIndex < 0)
        {
            RebuildDisplayApps();
            return;
        }

        hoverIndex = Math.Clamp(hoverIndex, 0, _displayApps.Count - 1);
        if (slotIndex != hoverIndex)
        {
            _displayApps.Move(slotIndex, hoverIndex);
        }
    }

    private List<AppEntryViewModel> GetVisibleAppsInCategory(AppCategory category, Guid draggingAppId)
        => Apps
            .Where(app => !app.IsDropSlot
                && app.Category == category
                && app.Enabled
                && app.Id != draggingAppId)
            .OrderBy(app => app.Order)
            .ToList();

    private int GetInsertIndexForOrder(AppEntryViewModel source)
        => Apps.Count(app => !app.IsDropSlot
            && app.Category == source.Category
            && app.Enabled
            && app.Id != source.Id
            && app.Order < source.Order);

    private void ApplyInsertMove(AppEntryViewModel source, int insertIndex, AppCategory targetCategory)
    {
        var categoryApps = Apps
            .Where(app => !app.IsDropSlot && app.Category == targetCategory && app.Enabled && app != source)
            .OrderBy(app => app.Order)
            .ToList();

        insertIndex = Math.Clamp(insertIndex, 0, categoryApps.Count);
        categoryApps.Insert(insertIndex, source);

        for (var i = 0; i < categoryApps.Count; i++)
        {
            categoryApps[i].SetReorderState(targetCategory, i);
        }
    }
}
