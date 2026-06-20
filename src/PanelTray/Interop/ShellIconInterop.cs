using System.Runtime.InteropServices;

namespace PanelTray.Interop;

internal static class ShellIconInterop
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeNormal = 0x00000080;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Shfileinfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref Shfileinfo psfi,
        uint cbFileInfo,
        uint uFlags);

    public static IntPtr ExtractIconHandle(string path, int requestedSize)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return IntPtr.Zero;
        }

        var isShellPath = path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
        if (!isShellPath && !File.Exists(path))
        {
            return IntPtr.Zero;
        }

        var info = new Shfileinfo();
        var sizeFlag = requestedSize <= 32 ? ShgfiSmallIcon : ShgfiLargeIcon;
        var flags = ShgfiIcon | sizeFlag;
        var attributes = 0u;

        if (isShellPath)
        {
            flags |= ShgfiUseFileAttributes;
            attributes = FileAttributeNormal;
        }

        var result = SHGetFileInfo(
            path,
            attributes,
            ref info,
            (uint)Marshal.SizeOf<Shfileinfo>(),
            flags);

        return result == IntPtr.Zero ? IntPtr.Zero : info.hIcon;
    }
}
