using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cropwell.Services;

namespace Cropwell.Dialogs;

/// <summary>Shows one monitor's frozen frame and lets the user drag a rectangle on it.</summary>
public partial class CaptureOverlay : Window
{
    internal const int MinimumSelection = 5;
    private static readonly IntPtr TopmostWindow = new(-1);
    private const uint NoActivate = 0x0010;

    private readonly MonitorCapture _monitor;
    private readonly TaskCompletionSource<BitmapSource?> _completion;
    private readonly List<CaptureOverlay> _overlays;
    private bool _closed;
    private bool _dragging;
    private Point _start;

    private CaptureOverlay(MonitorCapture monitor, TaskCompletionSource<BitmapSource?> completion, List<CaptureOverlay> overlays)
    {
        InitializeComponent();
        _monitor = monitor;
        _completion = completion;
        _overlays = overlays;
        Frame.Source = monitor.Image;
        SourceInitialized += (_, _) => ApplyBounds();
    }

    /// <summary>Returns the selected pixels, or null if the user cancels.</summary>
    public static Task<BitmapSource?> SelectAsync(IReadOnlyList<MonitorCapture> monitors)
    {
        var completion = new TaskCompletionSource<BitmapSource?>();
        var overlays = new List<CaptureOverlay>();
        overlays.AddRange(monitors.Select(m => new CaptureOverlay(m, completion, overlays)));
        foreach (var overlay in overlays) overlay.Show();
        var (x, y) = ScreenCapture.CursorPosition();
        (overlays.FirstOrDefault(o => Contains(o._monitor.Bounds, x, y)) ?? overlays[0]).Activate();
        return completion.Task;
    }

    /// <summary>Converts a drag in view units to a pixel rectangle clamped to the frame.</summary>
    internal static Int32Rect ToPixels(Point start, Point end, Size view, int pixelWidth, int pixelHeight)
    {
        var scaleX = pixelWidth / view.Width;
        var scaleY = pixelHeight / view.Height;
        var left = Math.Clamp((int)Math.Round(Math.Min(start.X, end.X) * scaleX), 0, pixelWidth);
        var right = Math.Clamp((int)Math.Round(Math.Max(start.X, end.X) * scaleX), 0, pixelWidth);
        var top = Math.Clamp((int)Math.Round(Math.Min(start.Y, end.Y) * scaleY), 0, pixelHeight);
        var bottom = Math.Clamp((int)Math.Round(Math.Max(start.Y, end.Y) * scaleY), 0, pixelHeight);
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    private static bool Contains(Int32Rect bounds, int x, int y) =>
        x >= bounds.X && x < bounds.X + bounds.Width && y >= bounds.Y && y < bounds.Y + bounds.Height;

    internal static bool IsTooSmall(Int32Rect selection) => selection.Width < MinimumSelection || selection.Height < MinimumSelection;

    // Position in physical pixels; WPF's Left/Top are in units that depend on which monitor the window is on.
    private void ApplyBounds()
    {
        var b = _monitor.Bounds;
        SetWindowPos(new WindowInteropHelper(this).Handle, TopmostWindow, b.X, b.Y, b.Width, b.Height, NoActivate);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(ApplyBounds);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
        Finish(null);
    }

    private void Finish(BitmapSource? result)
    {
        if (!_completion.TrySetResult(result)) return;
        foreach (var overlay in _overlays.Where(o => !o._closed)) overlay.Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Finish(null);
    }

    private void Surface_SizeChanged(object sender, SizeChangedEventArgs e) => ShowSelection(null);

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = Clamp(e.GetPosition(Surface));
        _dragging = true;
        Surface.CaptureMouse();
        ShowSelection(new Rect(_start, _start));
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging) ShowSelection(new Rect(_start, Clamp(e.GetPosition(Surface))));
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Surface.ReleaseMouseCapture();
        var image = _monitor.Image;
        var selection = ToPixels(_start, e.GetPosition(Surface), Surface.RenderSize, image.PixelWidth, image.PixelHeight);
        if (IsTooSmall(selection)) { ShowSelection(null); return; }
        Finish(ImageTransforms.Crop(image, selection));
    }

    private Point Clamp(Point point) =>
        new(Math.Clamp(point.X, 0, Surface.ActualWidth), Math.Clamp(point.Y, 0, Surface.ActualHeight));

    private void ShowSelection(Rect? selection)
    {
        var full = new RectangleGeometry(new Rect(Surface.RenderSize));
        if (selection is not Rect rect)
        {
            Dim.Data = full;
            SelectionBox.Visibility = Visibility.Collapsed;
            return;
        }
        Dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(rect));
        SelectionBox.Margin = new Thickness(rect.X, rect.Y, 0, 0);
        SelectionBox.Width = rect.Width;
        SelectionBox.Height = rect.Height;
        SelectionBox.Visibility = Visibility.Visible;
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
