using System.ComponentModel;
using System.Windows;
using PanelTray.ViewModels;

namespace PanelTray.Views;

public partial class SettingsWindow : Window
{
    private bool _allowClose;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public void AllowClose() => _allowClose = true;

    private void OnAddInstalledAppClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel settings)
        {
            settings.Panel.TryAddInstalledApp(this);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is SettingsViewModel settings)
        {
            settings.FlushPendingChanges();
        }

        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
