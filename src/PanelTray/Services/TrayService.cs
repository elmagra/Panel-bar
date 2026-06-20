using DrawingIcon = System.Drawing.Icon;
using Forms = System.Windows.Forms;

namespace PanelTray.Services;

public sealed class TrayService : ITrayService
{
    private Forms.NotifyIcon? _notifyIcon;
    private DrawingIcon? _trayIcon;

    public event EventHandler? TogglePanelRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? RestartRequested;
    public event EventHandler? ExitRequested;

    public void Initialize()
    {
        _trayIcon = CreateTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Mi Panel Tray",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _notifyIcon.MouseUp += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                TogglePanelRequested?.Invoke(this, EventArgs.Empty);
            }
        };

        _notifyIcon.ShowBalloonTip(
            2500,
            "Mi Panel Tray",
            "La app esta activa. Clic en el icono o Ctrl+Alt+P para abrir el panel.",
            Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir panel", null, (_, _) => TogglePanelRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Configuracion", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Reiniciar", null, (_, _) => RestartRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Salir", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        return menu;
    }

    private static DrawingIcon CreateTrayIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(System.Drawing.Color.Transparent);

        using var background = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 79, 70, 229));
        graphics.FillEllipse(background, 2, 2, 28, 28);

        using var foreground = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        using var font = new System.Drawing.Font("Segoe UI", 14, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
        var textSize = graphics.MeasureString("P", font);
        graphics.DrawString("P", font, foreground, (32 - textSize.Width) / 2 + 1, (32 - textSize.Height) / 2 - 1);

        return DrawingIcon.FromHandle(bitmap.GetHicon());
    }
}
