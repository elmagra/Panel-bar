using System.Windows.Media;
using PanelTray.Models;

namespace PanelTray.Services;

internal sealed class DropSlotIconService : IIconService
{
    public static DropSlotIconService Instance { get; } = new();

    public ImageSource GetIcon(AppEntry app, int requestedSize) => null!;
}
