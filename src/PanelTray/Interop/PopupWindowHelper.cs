using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace PanelTray.Interop;

internal static class PopupWindowHelper
{
    public static void TrySetTopmostForContextMenu(ContextMenu menu)
    {
        menu.UpdateLayout();
        for (var index = 0; index < menu.Items.Count; index++)
        {
            if (menu.ItemContainerGenerator.ContainerFromIndex(index) is Visual visual)
            {
                TrySetTopmost(visual);
                return;
            }
        }
    }

    public static void TrySetTopmostForPopup(Popup popup)
    {
        if (popup.Child is not DependencyObject child)
        {
            return;
        }

        if (child is FrameworkElement element && !element.IsLoaded)
        {
            element.Loaded += (_, _) => TrySetTopmost(element);
            return;
        }

        TrySetTopmost(child);
    }

    private static void TrySetTopmost(DependencyObject root)
    {
        if (PresentationSource.FromVisual(GetVisual(root)) is not HwndSource source
            || source.Handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            source.Handle,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNomove | NativeMethods.SwpNosize | NativeMethods.SwpNoactivate);
    }

    private static Visual? GetVisual(DependencyObject root)
    {
        if (root is Visual visual)
        {
            return visual;
        }

        if (root is ContextMenu { PlacementTarget: Visual target })
        {
            return target;
        }

        return null;
    }
}
