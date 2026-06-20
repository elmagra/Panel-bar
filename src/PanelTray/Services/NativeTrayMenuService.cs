using System.Reflection;
using PanelTray.Interop;
using PanelTray.Models;

namespace PanelTray.Services;

public sealed class NativeTrayMenuService
{
    private const int UiaNamePropertyId = 30005;
    private const int TreeScopeChildren = 1;
    private const int TreeScopeDescendants = 4;

    private static readonly Dictionary<string, string[]> KnownTooltips = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tailscale"] = ["Tailscale", "Tailscale Connected", "Connected"],
        ["NordVPN"] = ["NordVPN"],
        ["Bitdefender"] = ["Bitdefender"]
    };

    private readonly ILoggingService _logger;

    public NativeTrayMenuService(ILoggingService logger)
    {
        _logger = logger;
    }

    public bool TryShowContextMenu(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        return TryShowContextMenuInternal(BuildSearchPatterns(displayName, null));
    }

    public bool TryShowContextMenuForApp(AppEntry app)
        => TryShowContextMenuInternal(BuildSearchPatterns(app.DisplayName, app.ProcessName));

    private bool TryShowContextMenuInternal(string[] patterns)
    {
        if (patterns.Length == 0)
        {
            return false;
        }

        try
        {
            var automation = CreateAutomation();
            if (automation is null)
            {
                return false;
            }

            if (TryShowOnOverflowWindow(automation, patterns))
            {
                return true;
            }

            return TryShowFromDesktopTree(automation, patterns);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not open native tray menu.");
            return false;
        }
    }

    private static string[] BuildSearchPatterns(string displayName, string? processName)
    {
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            patterns.Add(displayName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(processName))
        {
            patterns.Add(CleanProcessName(processName));
        }

        if (!string.IsNullOrWhiteSpace(displayName)
            && KnownTooltips.TryGetValue(displayName.Trim(), out var known))
        {
            foreach (var pattern in known)
            {
                patterns.Add(pattern);
            }
        }

        return patterns
            .Where(pattern => pattern.Length >= 3)
            .ToArray();
    }

    private static string CleanProcessName(string processName)
    {
        processName = processName.Trim();
        return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
    }

    private static object? CreateAutomation()
    {
        var automationType = Type.GetTypeFromProgID("CUIAutomation.CUIAutomation");
        return automationType is null ? null : Activator.CreateInstance(automationType);
    }

    private static bool TryShowOnOverflowWindow(object automation, string[] patterns)
    {
        var outer = NativeMethods.FindWindow(NativeMethods.OverflowWindowClass, null);
        if (outer == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.ShowWindowAsync(outer, NativeMethods.SwShow);
        try
        {
            return TryShowContextMenuInWindow(automation, outer, patterns);
        }
        finally
        {
            NativeMethods.ShowWindowAsync(outer, NativeMethods.SwHide);
        }
    }

    private static bool TryShowFromDesktopTree(object automation, string[] patterns)
    {
        dynamic auto = automation;
        dynamic root = auto.GetRootElement();
        foreach (var pattern in patterns)
        {
            dynamic condition = auto.CreatePropertyCondition(UiaNamePropertyId, pattern);
            dynamic element = root.FindFirst(TreeScopeDescendants, condition);
            if (element is null)
            {
                continue;
            }

            if (InvokeShowContextMenu(element))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryShowContextMenuInWindow(object automation, IntPtr windowHandle, string[] patterns)
    {
        dynamic auto = automation;
        var inner = NativeMethods.FindWindowEx(
            windowHandle,
            IntPtr.Zero,
            NativeMethods.OverflowBridgeClass,
            null);

        var targetHandle = inner != IntPtr.Zero ? inner : windowHandle;
        dynamic container = auto.ElementFromHandle(targetHandle);
        if (container is null)
        {
            return false;
        }

        dynamic icons = container.FindAll(TreeScopeChildren, auto.CreateTrueCondition());
        for (var i = 0; i < (int)icons.Length; i++)
        {
            dynamic icon = icons.GetElement(i);
            if (!NameMatches(icon, patterns))
            {
                continue;
            }

            if (InvokeShowContextMenu(icon))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InvokeShowContextMenu(object element)
    {
        foreach (var iface in element.GetType().GetInterfaces())
        {
            var method = iface.GetMethod("ShowContextMenu", Type.EmptyTypes);
            if (method is null)
            {
                continue;
            }

            method.Invoke(element, null);
            return true;
        }

        return false;
    }

    private static bool NameMatches(object element, string[] patterns)
    {
        var name = element.GetType().InvokeMember(
            "get_CurrentName",
            BindingFlags.GetProperty,
            null,
            element,
            null) as string ?? string.Empty;

        return patterns.Any(pattern => name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
