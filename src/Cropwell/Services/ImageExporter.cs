using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.ObjectModel;

namespace Cropwell.Services;

public enum ExportFormat { Png, WebP, Jpeg, Gif }

public static class ImageExporter
{
    private static readonly Guid WebPContainerFormat = new("e094b0e2-67f2-45b3-b0ea-115337ca7cf3");

    public static string Extension(ExportFormat format) => format switch
    {
        ExportFormat.Png => ".png",
        ExportFormat.WebP => ".webp",
        ExportFormat.Jpeg => ".jpg",
        ExportFormat.Gif => ".gif",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static void Export(BitmapSource image, string destination, string? sourcePath, ExportFormat format)
    {
        if (sourcePath is not null && Path.GetFullPath(destination).Equals(Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cropwell will not overwrite the source image.");

        BitmapEncoder encoder = format switch
        {
            ExportFormat.Png => new PngBitmapEncoder(),
            ExportFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = 90 },
            ExportFormat.Gif => new GifBitmapEncoder(),
            ExportFormat.WebP => CreateWebPEncoder(),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        var output = format == ExportFormat.Jpeg ? ApiImageEncoder.FlattenWhite(image) : image;
        BitmapSource frameSource = output;
        if (format == ExportFormat.Gif)
        {
            var palette = new BitmapPalette(output, 256);
            frameSource = new FormatConvertedBitmap(output, PixelFormats.Indexed8, palette, 0.5);
        }
        ReadOnlyCollection<ColorContext>? colorContexts = format == ExportFormat.Gif
            ? null
            : new ReadOnlyCollection<ColorContext>([new ColorContext(PixelFormats.Bgra32)]);
        encoder.Frames.Add(BitmapFrame.Create(frameSource, null, null, colorContexts));
        using var stream = File.Open(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    private static BitmapEncoder CreateWebPEncoder()
    {
        try { return BitmapEncoder.Create(WebPContainerFormat); }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
        {
            throw new InvalidOperationException("The installed WebP codec does not support export.", ex);
        }
    }
}
