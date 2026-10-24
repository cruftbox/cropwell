using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cropwell.Services;

public static class ImageTransforms
{
    public static BitmapSource Crop(BitmapSource source, Int32Rect rect)
    {
        var result = new CroppedBitmap(source, rect);
        result.Freeze();
        return result;
    }

    public static (int Width, int Height) AspectSize(int sourceWidth, int sourceHeight, int? width, int? height)
    {
        if (width is null == (height is null)) throw new ArgumentException("Specify exactly one dimension.");
        if (width is not null)
            return (width.Value, Math.Max(1, (int)Math.Round(width.Value * sourceHeight / (double)sourceWidth, MidpointRounding.AwayFromZero)));
        return (Math.Max(1, (int)Math.Round(height!.Value * sourceWidth / (double)sourceHeight, MidpointRounding.AwayFromZero)), height.Value);
    }

    public static BitmapSource ResizeLanczos(BitmapSource source, int targetWidth, int targetHeight)
    {
        if (targetWidth is < 1 or > 16384 || targetHeight is < 1 or > 16384)
            throw new ArgumentOutOfRangeException(nameof(targetWidth), "Dimensions must be between 1 and 16384 pixels.");

        var bgra = ConvertFormat(source, PixelFormats.Bgra32);
        var sourceStride = bgra.PixelWidth * 4;
        var input = new byte[sourceStride * bgra.PixelHeight];
        bgra.CopyPixels(input, sourceStride, 0);
        var horizontal = ResampleHorizontal(input, bgra.PixelWidth, bgra.PixelHeight, targetWidth);
        var output = ResampleVertical(horizontal, targetWidth, bgra.PixelHeight, targetHeight);
        var result = BitmapSource.Create(targetWidth, targetHeight, 96, 96, PixelFormats.Bgra32, null, output, targetWidth * 4);
        result.Freeze();
        return result;
    }

    public static BitmapSource ConvertFormat(BitmapSource source, PixelFormat format)
    {
        if (source.Format == format) return source;
        var converted = new FormatConvertedBitmap(source, format, null, 0);
        converted.Freeze();
        return converted;
    }

    private static byte[] ResampleHorizontal(byte[] input, int sourceWidth, int height, int targetWidth)
    {
        var output = new byte[targetWidth * height * 4];
        var scale = sourceWidth / (double)targetWidth;
        var radius = scale > 1 ? 3 * scale : 3;
        var sum = new double[4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < targetWidth; x++)
        {
            var center = (x + .5) * scale - .5;
            var left = Math.Max(0, (int)Math.Ceiling(center - radius));
            var right = Math.Min(sourceWidth - 1, (int)Math.Floor(center + radius));
            Array.Clear(sum);
            double weights = 0;
            for (var sx = left; sx <= right; sx++)
            {
                var weight = Lanczos((center - sx) / Math.Max(1, scale));
                weights += weight;
                var offset = (y * sourceWidth + sx) * 4;
                for (var c = 0; c < 4; c++) sum[c] += input[offset + c] * weight;
            }
            var destination = (y * targetWidth + x) * 4;
            for (var c = 0; c < 4; c++) output[destination + c] = (byte)Math.Clamp(Math.Round(sum[c] / weights), 0, 255);
        }
        return output;
    }

    private static byte[] ResampleVertical(byte[] input, int width, int sourceHeight, int targetHeight)
    {
        var output = new byte[width * targetHeight * 4];
        var scale = sourceHeight / (double)targetHeight;
        var radius = scale > 1 ? 3 * scale : 3;
        var sum = new double[4];
        for (var y = 0; y < targetHeight; y++)
        {
            var center = (y + .5) * scale - .5;
            var top = Math.Max(0, (int)Math.Ceiling(center - radius));
            var bottom = Math.Min(sourceHeight - 1, (int)Math.Floor(center + radius));
            for (var x = 0; x < width; x++)
            {
                Array.Clear(sum);
                double weights = 0;
                for (var sy = top; sy <= bottom; sy++)
                {
                    var weight = Lanczos((center - sy) / Math.Max(1, scale));
                    weights += weight;
                    var offset = (sy * width + x) * 4;
                    for (var c = 0; c < 4; c++) sum[c] += input[offset + c] * weight;
                }
                var destination = (y * width + x) * 4;
                for (var c = 0; c < 4; c++) output[destination + c] = (byte)Math.Clamp(Math.Round(sum[c] / weights), 0, 255);
            }
        }
        return output;
    }

    private static double Lanczos(double x)
    {
        x = Math.Abs(x);
        if (x < double.Epsilon) return 1;
        if (x >= 3) return 0;
        return Math.Sin(Math.PI * x) * Math.Sin(Math.PI * x / 3) / (Math.PI * Math.PI * x * x / 3);
    }
}
