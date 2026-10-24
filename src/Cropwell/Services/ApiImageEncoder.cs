using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cropwell.Services;

public sealed record EncodedApiImage(string MediaType, string Base64, int Width, int Height);

public static class ApiImageEncoder
{
    public const int MaximumBase64Characters = 5 * 1024 * 1024;

    public static EncodedApiImage Encode(BitmapSource source) => Encode(source, MaximumBase64Characters);

    internal static EncodedApiImage Encode(BitmapSource source, int maximumBase64Characters)
    {
        var image = FlattenWhite(source);
        var longEdge = Math.Max(image.PixelWidth, image.PixelHeight);
        if (longEdge > 2048)
        {
            var scale = 2048d / longEdge;
            image = ImageTransforms.ResizeLanczos(image, Math.Max(1, (int)Math.Round(image.PixelWidth * scale)), Math.Max(1, (int)Math.Round(image.PixelHeight * scale)));
        }

        var png = EncodeWith(new PngBitmapEncoder(), image);
        var png64 = Convert.ToBase64String(png);
        if (png64.Length <= maximumBase64Characters) return new("image/png", png64, image.PixelWidth, image.PixelHeight);

        while (true)
        {
            var jpegEncoder = new JpegBitmapEncoder { QualityLevel = 85 };
            var jpeg = EncodeWith(jpegEncoder, image);
            var jpeg64 = Convert.ToBase64String(jpeg);
            if (jpeg64.Length <= maximumBase64Characters) return new("image/jpeg", jpeg64, image.PixelWidth, image.PixelHeight);
            if (Math.Max(image.PixelWidth, image.PixelHeight) < 512)
                throw new InvalidOperationException("The image could not be reduced below the provider upload limit.");
            image = ImageTransforms.ResizeLanczos(image, Math.Max(1, (int)Math.Floor(image.PixelWidth * .75)), Math.Max(1, (int)Math.Floor(image.PixelHeight * .75)));
        }
    }

    public static BitmapSource FlattenWhite(BitmapSource source)
    {
        var bgra = ImageTransforms.ConvertFormat(source, PixelFormats.Bgra32);
        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            if (alpha < 255)
            {
                pixels[i] = Composite(pixels[i], alpha);
                pixels[i + 1] = Composite(pixels[i + 1], alpha);
                pixels[i + 2] = Composite(pixels[i + 2], alpha);
                pixels[i + 3] = 255;
            }
        }
        var result = BitmapSource.Create(bgra.PixelWidth, bgra.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private static byte Composite(byte value, byte alpha) => (byte)((value * alpha + 255 * (255 - alpha) + 127) / 255);

    private static byte[] EncodeWith(BitmapEncoder encoder, BitmapSource image)
    {
        encoder.Frames.Add(BitmapFrame.Create(image, null, null, null));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
