using System.Windows;
using System.Windows.Input;
using PanelTray.ViewModels;

namespace PanelTray.Views;

public partial class InstalledAppPickerWindow : Window
{
    public InstalledAppPickerWindow(InstalledAppPickerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public InstalledAppPickerViewModel ViewModel => (InstalledAppPickerViewModel)DataContext;

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedApp is null)
        {
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedApp is null)
        {
            return;
        }

        DialogResult = true;
        Close();
    }
}
