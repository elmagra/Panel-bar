using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace PanelTray.Interop;

internal static class ShellItemImageInterop
{
    private static readonly Guid ShellItemGuid = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    [Flags]
    private enum Siigbf : uint
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        IconOnly = 0x04
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80a4-8a5c0c442c23")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, Siigbf flags, out IntPtr bitmapHandle);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object shellItem);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public static BitmapSource? TryGetImage(string shellPath, int requestedSize)
    {
        if (string.IsNullOrWhiteSpace(shellPath)
            || !shellPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var shellItemGuid = ShellItemGuid;
            var hr = SHCreateItemFromParsingName(
                shellPath,
                IntPtr.Zero,
                ref shellItemGuid,
                out var item);

            if (hr != 0 || item is not IShellItemImageFactory factory)
            {
                return null;
            }

            var size = Math.Clamp(requestedSize, 16, 256);
            hr = factory.GetImage(
                new NativeSize { Width = size, Height = size },
                Siigbf.IconOnly | Siigbf.BiggerSizeOk,
                out var bitmapHandle);

            if (hr != 0 || bitmapHandle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    bitmapHandle,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(bitmapHandle);
            }
        }
        catch
        {
            return null;
        }
    }
}
