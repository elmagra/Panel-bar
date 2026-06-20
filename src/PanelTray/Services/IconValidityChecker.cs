using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PanelTray.Services;

public static class IconValidityChecker
{
    public static bool IsUsable(ImageSource? source)
    {
        if (source is not BitmapSource bitmap)
        {
            return source is not null;
        }

        if (bitmap.PixelWidth < 2 || bitmap.PixelHeight < 2)
        {
            return false;
        }

        var normalized = bitmap.Format == PixelFormats.Pbgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);

        if (!ReferenceEquals(normalized, bitmap))
        {
            normalized.Freeze();
        }

        var width = normalized.PixelWidth;
        var height = normalized.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        normalized.CopyPixels(pixels, stride, 0);

        var opaquePixels = 0;
        var colorfulPixels = 0;

        for (var index = 0; index < pixels.Length; index += 4)
        {
            var alpha = pixels[index + 3];
            if (alpha <= 24)
            {
                continue;
            }

            opaquePixels++;
            var red = pixels[index + 2];
            var green = pixels[index + 1];
            var blue = pixels[index];

            var max = Math.Max(red, Math.Max(green, blue));
            var min = Math.Min(red, Math.Min(green, blue));
            var isNearWhite = red >= 235 && green >= 235 && blue >= 235;
            var hasColor = !isNearWhite || max - min >= 18;

            if (hasColor)
            {
                colorfulPixels++;
            }
        }

        var totalPixels = width * height;
        if (opaquePixels < totalPixels * 0.04)
        {
            return false;
        }

        if (colorfulPixels < Math.Max(12, opaquePixels * 0.08))
        {
            return false;
        }

        return true;
    }
}
