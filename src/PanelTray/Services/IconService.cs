using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PanelTray.Interop;
using PanelTray.Models;

namespace PanelTray.Services;

public sealed class IconService : IIconService
{
    private const double VisualInsetRatio = 0.78;

    public ImageSource GetIcon(AppEntry app, int requestedSize)
    {
        requestedSize = Math.Max(16, requestedSize);

        foreach (var candidate in GetIconCandidates(app))
        {
            var icon = TryExtractIcon(candidate, requestedSize);
            if (!IconValidityChecker.IsUsable(icon))
            {
                continue;
            }

            return FitIconToSquare(icon!, requestedSize);
        }

        return CreateFallbackIcon(app.DisplayName, requestedSize);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PrivateExtractIcons(
        string lpszFile,
        int nIconIndex,
        int cxIcon,
        int cyIcon,
        IntPtr[]? phicon,
        IntPtr[]? piconid,
        uint nIcons,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static IEnumerable<string> GetIconCandidates(AppEntry app)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in BuildCandidates(app))
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> BuildCandidates(AppEntry app)
    {
        foreach (var candidate in StoreAppIconResolver.GetModernAppIconCandidates(
                     app.DisplayName,
                     app.ExecutablePath,
                     app.IconPath))
        {
            yield return candidate;
        }

        foreach (var resolved in ExpandIconPathChain(app.IconPath))
        {
            yield return resolved;
        }

        foreach (var resolved in ExpandIconPathChain(app.ExecutablePath))
        {
            yield return resolved;
        }

        var known = ShortcutResolver.FindKnownExecutable(app.DisplayName);
        if (!string.IsNullOrWhiteSpace(known))
        {
            foreach (var resolved in ExpandIconPathChain(known))
            {
                yield return resolved;
            }
        }

        var launcherIcon = ShortcutResolver.ResolveLauncherIconPath(
            Environment.ExpandEnvironmentVariables(app.ExecutablePath ?? string.Empty));
        if (!string.IsNullOrWhiteSpace(launcherIcon))
        {
            foreach (var resolved in ExpandIconPathChain(launcherIcon))
            {
                yield return resolved;
            }
        }

        var shortcut = ShortcutResolver.FindShortcut(app.DisplayName);
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            var shortcutIcon = ShortcutResolver.ResolveShortcutIconPath(shortcut);
            if (!string.IsNullOrWhiteSpace(shortcutIcon))
            {
                foreach (var resolved in ExpandIconPathChain(shortcutIcon))
                {
                    yield return resolved;
                }
            }
        }

    }

    private static IEnumerable<string> ExpandIconPathChain(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (expanded.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var iconPath = ShortcutResolver.ResolveShortcutIconPath(expanded);
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                yield return iconPath;
            }

            yield break;
        }

        if (StoreAppIconResolver.IsShellIconPath(expanded))
        {
            yield return expanded;
            yield break;
        }

        yield return expanded;
    }

    private static ImageSource? TryExtractIcon(string path, int requestedSize)
    {
        if (string.IsNullOrWhiteSpace(path) || path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (StoreAppIconResolver.IsShellIconPath(path))
        {
            var shellItemIcon = ShellItemImageInterop.TryGetImage(path, requestedSize);
            if (IconValidityChecker.IsUsable(shellItemIcon))
            {
                return shellItemIcon;
            }

            var legacyShellIcon = ExtractShellIcon(path, requestedSize);
            if (IconValidityChecker.IsUsable(legacyShellIcon))
            {
                return legacyShellIcon;
            }

            return null;
        }

        var (filePath, iconIndex) = ShortcutResolver.ParseIconReference(path);
        if (string.IsNullOrWhiteSpace(filePath) || filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!File.Exists(filePath))
        {
            return null;
        }

        if (IsImageFile(filePath))
        {
            if (filePath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                return LoadIco(filePath, iconIndex);
            }

            return LoadBitmap(filePath, requestedSize);
        }

        var indexedIcon = ExtractIndexedIcon(filePath, iconIndex, requestedSize);
        if (IconValidityChecker.IsUsable(indexedIcon))
        {
            return indexedIcon;
        }

        var associatedIcon = ExtractAssociatedIcon(filePath);
        if (IconValidityChecker.IsUsable(associatedIcon))
        {
            return associatedIcon;
        }

        var shellIcon = ExtractShellIcon(filePath, requestedSize);
        return IconValidityChecker.IsUsable(shellIcon) ? shellIcon : null;
    }

    private static ImageSource? ExtractIndexedIcon(string path, int iconIndex, int requestedSize)
    {
        try
        {
            var icons = new IntPtr[1];
            var count = PrivateExtractIcons(
                path,
                iconIndex,
                requestedSize,
                requestedSize,
                icons,
                null,
                1,
                0);

            if (count == 0 || icons[0] == IntPtr.Zero)
            {
                return null;
            }

            using var icon = Icon.FromHandle(icons[0]);
            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            DestroyIcon(icons[0]);
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? ExtractShellIcon(string path, int requestedSize)
    {
        try
        {
            var shellIcon = ShellIconInterop.ExtractIconHandle(path, requestedSize);
            if (shellIcon == IntPtr.Zero)
            {
                return null;
            }

            using var icon = Icon.FromHandle(shellIcon);
            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            DestroyIcon(shellIcon);
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? ExtractAssociatedIcon(string path)
    {
        try
        {
            var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            using (icon)
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource FitIconToSquare(ImageSource source, int size)
    {
        if (source is DrawingImage drawingImage)
        {
            source = RenderDrawingToBitmap(drawingImage, size);
        }

        if (source is not BitmapSource bitmap)
        {
            return source;
        }

        var content = CropToVisibleContent(bitmap);
        var maxSide = size * VisualInsetRatio;
        var scale = Math.Min(maxSide / content.PixelWidth, maxSide / content.PixelHeight);
        var width = content.PixelWidth * scale;
        var height = content.PixelHeight * scale;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(content, new Rect((size - width) / 2, (size - height) / 2, width, height));
        }

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static BitmapSource CropToVisibleContent(BitmapSource bitmap)
    {
        var bounds = GetOpaqueContentBounds(bitmap);
        if (bounds is not { Width: > 0, Height: > 0 } rect)
        {
            return bitmap;
        }

        if (rect.Width >= bitmap.PixelWidth && rect.Height >= bitmap.PixelHeight)
        {
            return bitmap;
        }

        var cropped = new CroppedBitmap(bitmap, rect);
        cropped.Freeze();
        return cropped;
    }

    private static Int32Rect? GetOpaqueContentBounds(BitmapSource bitmap)
    {
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        if (width == 0 || height == 0)
        {
            return null;
        }

        var source = bitmap.Format == PixelFormats.Pbgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);

        if (!ReferenceEquals(source, bitmap))
        {
            source.Freeze();
        }

        var stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);

        const byte alphaThreshold = 24;
        var minX = width;
        var minY = height;
        var maxX = 0;
        var maxY = 0;
        var found = false;

        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                if (pixels[row + (x * 4) + 3] <= alphaThreshold)
                {
                    continue;
                }

                found = true;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return found
            ? new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1)
            : null;
    }

    private static BitmapSource RenderDrawingToBitmap(DrawingImage drawingImage, int size)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(drawingImage, new Rect(0, 0, size, size));
        }

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase);
    }

    private static ImageSource? LoadIco(string path, int iconIndex)
    {
        try
        {
            var indexed = ExtractIndexedIcon(path, iconIndex, 256);
            if (indexed is not null)
            {
                return indexed;
            }

            using var icon = Icon.ExtractAssociatedIcon(path) ?? new Icon(path);
            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage LoadBitmap(string path, int requestedSize)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = requestedSize;
        image.DecodePixelHeight = requestedSize;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static ImageSource CreateFallbackIcon(string displayName, int size)
    {
        var initial = string.IsNullOrWhiteSpace(displayName)
            ? "?"
            : displayName.Trim()[0].ToString().ToUpperInvariant();

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(79, 70, 229)),
            null,
            new RectangleGeometry(new Rect(0, 0, size, size), 10, 10)));

        var text = new FormattedText(
            initial,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            Math.Max(14, size * 0.42),
            System.Windows.Media.Brushes.White,
            1.25);

        group.Children.Add(new GeometryDrawing(
            System.Windows.Media.Brushes.White,
            null,
            text.BuildGeometry(new System.Windows.Point((size - text.Width) / 2, (size - text.Height) / 2))));
        group.Freeze();
        return new DrawingImage(group);
    }
}
