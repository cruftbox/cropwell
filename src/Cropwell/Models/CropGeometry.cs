using System.Windows;

namespace Cropwell.Models;

public enum CropMode { None, Square, Portrait, Free }

public static class CropGeometry
{
    public const int MinimumShortSide = 16;

    public static Int32Rect InitialSelection(CropMode mode, int imageWidth, int imageHeight)
    {
        if (imageWidth < 1 || imageHeight < 1) return Int32Rect.Empty;
        return mode switch
        {
            CropMode.Square => Centered(imageWidth, imageHeight, 1, 1),
            CropMode.Portrait => Centered(imageWidth, imageHeight, 9, 16),
            CropMode.Free => new Int32Rect(0, 0, imageWidth, imageHeight),
            _ => Int32Rect.Empty
        };
    }

    public static Int32Rect FromDrag(Point start, Point end, CropMode mode, int imageWidth, int imageHeight)
    {
        var x1 = Math.Clamp((int)Math.Round(Math.Min(start.X, end.X)), 0, imageWidth);
        var y1 = Math.Clamp((int)Math.Round(Math.Min(start.Y, end.Y)), 0, imageHeight);
        var width = Math.Max(1, Math.Abs((int)Math.Round(end.X - start.X)));
        var height = Math.Max(1, Math.Abs((int)Math.Round(end.Y - start.Y)));

        if (mode is CropMode.Square or CropMode.Portrait)
        {
            var rw = mode == CropMode.Square ? 1 : 9;
            var rh = mode == CropMode.Square ? 1 : 16;
            var k = Math.Max(MinimumShortSide / Math.Min(rw, rh), Math.Min(width / rw, height / rh));
            k = Math.Max(1, k);
            width = rw * k;
            height = rh * k;
        }

        width = Math.Clamp(width, 1, imageWidth);
        height = Math.Clamp(height, 1, imageHeight);
        x1 = Math.Min(x1, imageWidth - width);
        y1 = Math.Min(y1, imageHeight - height);
        return new Int32Rect(x1, y1, width, height);
    }

    public static Int32Rect Move(Int32Rect rect, int dx, int dy, int imageWidth, int imageHeight) =>
        new(Math.Clamp(rect.X + dx, 0, imageWidth - rect.Width),
            Math.Clamp(rect.Y + dy, 0, imageHeight - rect.Height), rect.Width, rect.Height);

    public static Int32Rect Clamp(Int32Rect rect, CropMode mode, int imageWidth, int imageHeight)
    {
        var minW = mode == CropMode.Portrait ? 9 : MinimumShortSide;
        var minH = mode == CropMode.Portrait ? 16 : MinimumShortSide;
        var width = Math.Clamp(rect.Width, Math.Min(minW, imageWidth), imageWidth);
        var height = Math.Clamp(rect.Height, Math.Min(minH, imageHeight), imageHeight);

        if (mode is CropMode.Square or CropMode.Portrait)
        {
            var rw = mode == CropMode.Square ? 1 : 9;
            var rh = mode == CropMode.Square ? 1 : 16;
            var maxK = Math.Min(imageWidth / rw, imageHeight / rh);
            var minK = Math.Max(1, (int)Math.Ceiling(MinimumShortSide / (double)Math.Min(rw, rh)));
            var k = Math.Clamp((int)Math.Round(Math.Min(width / (double)rw, height / (double)rh)), Math.Min(minK, maxK), maxK);
            width = rw * k;
            height = rh * k;
        }

        return new Int32Rect(Math.Clamp(rect.X, 0, imageWidth - width),
            Math.Clamp(rect.Y, 0, imageHeight - height), width, height);
    }

    private static Int32Rect Centered(int width, int height, int rw, int rh)
    {
        var k = Math.Min(width / rw, height / rh);
        var w = rw * k;
        var h = rh * k;
        return new Int32Rect((width - w) / 2, (height - h) / 2, w, h);
    }
}
