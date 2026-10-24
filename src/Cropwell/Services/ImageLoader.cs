using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cropwell.Services;

public static class ImageLoader
{
    public static BitmapSource LoadNormalized(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new InvalidDataException("The file contains no image frames.");
        BitmapSource source = decoder.Frames[0];

        if (decoder.Frames[0].ColorContexts is { Count: > 0 } contexts)
        {
            try
            {
                var destination = new ColorContext(PixelFormats.Bgra32);
                source = new ColorConvertedBitmap(source, contexts[0], destination, PixelFormats.Bgra32);
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
            {
                AppLog.Warning("Embedded ICC profile could not be converted; treating pixels as sRGB.", ex);
            }
        }

        source = ApplyExifOrientation(source, ReadOrientation(decoder.Frames[0]));
        var normalized = ImageTransforms.ConvertFormat(source, PixelFormats.Bgra32);
        var stride = normalized.PixelWidth * 4;
        var pixels = new byte[stride * normalized.PixelHeight];
        normalized.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(normalized.PixelWidth, normalized.PixelHeight,
            normalized.DpiX, normalized.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private static ushort ReadOrientation(BitmapFrame frame)
    {
        if (frame.Metadata is not BitmapMetadata metadata) return 1;
        try { return Convert.ToUInt16(metadata.GetQuery("/app1/ifd/{ushort=274}") ?? 1); }
        catch { return 1; }
    }

    private static BitmapSource ApplyExifOrientation(BitmapSource source, ushort orientation)
    {
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => Group(new ScaleTransform(-1, 1), new RotateTransform(90)),
            6 => new RotateTransform(90),
            7 => Group(new ScaleTransform(-1, 1), new RotateTransform(270)),
            8 => new RotateTransform(270),
            _ => null
        };
        if (transform is null) return source;
        var result = new TransformedBitmap(source, transform);
        result.Freeze();
        return result;
    }

    private static Transform Group(params Transform[] transforms)
    {
        var group = new TransformGroup();
        foreach (var transform in transforms) group.Children.Add(transform);
        return group;
    }
}
