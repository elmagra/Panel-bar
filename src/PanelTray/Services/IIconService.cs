using System.Windows.Media;
using PanelTray.Models;

namespace PanelTray.Services;

public interface IIconService
{
    ImageSource GetIcon(AppEntry app, int requestedSize);
}
