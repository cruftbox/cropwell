using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cropwell.Services;

/// <summary>A frozen image of one monitor. Bounds are physical pixels in virtual-screen coordinates.</summary>
public sealed record MonitorCapture(Int32Rect Bounds, BitmapSource Image);

public static class ScreenCapture
{
    private const int SrcCopy = 0x00CC0020;
    private const int CaptureBlt = 0x40000000;

    public static IReadOnlyList<MonitorCapture> CaptureMonitors()
    {
        var bounds = new List<Int32Rect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr _, IntPtr _, ref NativeRect rect, IntPtr _) =>
        {
            bounds.Add(new Int32Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
            return true;
        }, IntPtr.Zero);
        if (bounds.Count == 0) throw new InvalidOperationException("No monitors were found.");
        return bounds.Select(b => new MonitorCapture(b, Capture(b))).ToList();
    }

    public static (int X, int Y) CursorPosition() =>
        GetCursorPos(out var point) ? (point.X, point.Y) : (0, 0);

    private static BitmapSource Capture(Int32Rect bounds)
    {
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, bounds.Width, bounds.Height);
        var previous = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, bounds.Width, bounds.Height, screen, bounds.X, bounds.Y, SrcCopy | CaptureBlt))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            SelectObject(memory, previous);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            return ToOpaqueBgra(source);
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    // GDI leaves the fourth byte of each 32-bit pixel undefined, so it is forced to fully opaque.
    internal static BitmapSource ToOpaqueBgra(BitmapSource source)
    {
        var bgr = source.Format.BitsPerPixel == 32 ? source : ImageTransforms.ConvertFormat(source, PixelFormats.Bgr32);
        var stride = bgr.PixelWidth * 4;
        var pixels = new byte[stride * bgr.PixelHeight];
        bgr.CopyPixels(pixels, stride, 0);
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var result = BitmapSource.Create(bgr.PixelWidth, bgr.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int operation);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr gdiObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
