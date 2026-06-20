using System.Collections.ObjectModel;
using System.Windows.Input;
using PanelTray.Models;
using PanelTray.Services;

namespace PanelTray.ViewModels;

public sealed class InstalledAppPickerViewModel : ViewModelBase
{
    private readonly InstalledAppDiscoveryService _discoveryService;
    private readonly Func<IEnumerable<string>> _existingLaunchKeysProvider;
    private readonly List<InstalledAppCandidate> _allApps = new();
    private string _searchText = string.Empty;
    private InstalledAppCandidate? _selectedApp;
    private bool _isLoading = true;
    private string _statusText = "Buscando aplicaciones instaladas...";

    public InstalledAppPickerViewModel(
        InstalledAppDiscoveryService discoveryService,
        Func<IEnumerable<string>> existingLaunchKeysProvider)
    {
        _discoveryService = discoveryService;
        _existingLaunchKeysProvider = existingLaunchKeysProvider;
        FilteredApps = new ObservableCollection<InstalledAppCandidate>();
        AddCommand = new RelayCommand(() => { }, () => SelectedApp is not null);
        _ = LoadAsync();
    }

    public ObservableCollection<InstalledAppCandidate> FilteredApps { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public InstalledAppCandidate? SelectedApp
    {
        get => _selectedApp;
        set
        {
            if (SetProperty(ref _selectedApp, value))
            {
                (AddCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public ICommand AddCommand { get; }

    private async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "Buscando aplicaciones instaladas...";

            var apps = await _discoveryService.DiscoverAsync(_existingLaunchKeysProvider());
            _allApps.Clear();
            _allApps.AddRange(apps);
            ApplyFilter();
            StatusText = apps.Count == 0
                ? "No se encontraron aplicaciones nuevas para anadir."
                : $"{apps.Count} aplicaciones disponibles.";
        }
        catch (Exception)
        {
            StatusText = "No se pudo cargar la lista de aplicaciones.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        FilteredApps.Clear();
        var query = _searchText.Trim();

        foreach (var app in _allApps)
        {
            if (query.Length > 0
                && !app.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !app.Detail.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FilteredApps.Add(app);
        }

        if (!IsLoading)
        {
            StatusText = FilteredApps.Count == 0
                ? "Ninguna aplicacion coincide con la busqueda."
                : $"{FilteredApps.Count} aplicaciones mostradas.";
        }
    }
}
