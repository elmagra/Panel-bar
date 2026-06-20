using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using PanelTray.Services;

namespace PanelTray.Views.Controls;

public partial class TrayAppFlyout : UserControl
{
    public static readonly DependencyProperty SnapshotProperty =
        DependencyProperty.Register(nameof(Snapshot), typeof(TailscaleStatusSnapshot), typeof(TrayAppFlyout));

    public TrayAppFlyout()
    {
        InitializeComponent();
    }

    public TailscaleStatusSnapshot? Snapshot
    {
        get => (TailscaleStatusSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public event EventHandler? NativeMenuRequested;
    public event EventHandler? ExitRequested;

    private void OnNativeMenuClick(object sender, RoutedEventArgs e) => NativeMenuRequested?.Invoke(this, EventArgs.Empty);

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var version = Snapshot?.Version;
        MessageBox.Show(
            string.IsNullOrWhiteSpace(version) ? "Tailscale" : $"Tailscale {version}",
            "Tailscale",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var exe = @"C:\Program Files\Tailscale\tailscale.exe";
            if (!File.Exists(exe))
            {
                exe = Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Tailscale\tailscale.exe");
            }

            if (File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "down",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
        }
        catch
        {
            // ignore
        }
    }
}
