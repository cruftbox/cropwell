using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cropwell.Models;

namespace Cropwell.Controls;

public sealed class ImageCanvas : FrameworkElement
{
    private enum DragKind { None, Draw, Move, Left, Top, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }
    private BitmapSource? _image;
    private double _zoom = 1;
    private CropMode _cropMode;
    private Int32Rect _selection = Int32Rect.Empty;
    private DragKind _drag;
    private Point _start;
    private Int32Rect _startSelection;

    public event EventHandler? SelectionChanged;
    public BitmapSource? Image
    {
        get => _image;
        set { _image = value; _selection = Int32Rect.Empty; InvalidateMeasure(); InvalidateVisual(); }
    }
    public double Zoom
    {
        get => _zoom;
        set { _zoom = Math.Clamp(value, .1, 8); InvalidateMeasure(); InvalidateVisual(); }
    }
    public CropMode CropMode
    {
        get => _cropMode;
        set
        {
            _cropMode = value;
            _selection = _image is null ? Int32Rect.Empty : CropGeometry.InitialSelection(value, _image.PixelWidth, _image.PixelHeight);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }
    public Int32Rect Selection => _selection;

    public void ClearSelection()
    {
        _cropMode = CropMode.None;
        _selection = Int32Rect.Empty;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => _image is null
        ? new Size(1, 1)
        : new Size(_image.PixelWidth * Zoom, _image.PixelHeight * Zoom);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(210, 210, 210)), null, new Rect(RenderSize));
        if (_image is null) return;
        RenderOptions.SetBitmapScalingMode(this, Zoom >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        dc.DrawImage(_image, new Rect(0, 0, _image.PixelWidth * Zoom, _image.PixelHeight * Zoom));
        if (_selection.IsEmpty) return;

        var rect = ToScreen(_selection);
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)), new RectangleGeometry(rect));
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(105, 0, 0, 0)), null, outside);
        var pen = new Pen(Brushes.White, 1) { DashStyle = DashStyles.Dash };
        dc.DrawRectangle(null, pen, rect);
        foreach (var point in HandlePoints(rect)) dc.DrawRectangle(Brushes.White, new Pen(Brushes.Black, 1), new Rect(point.X - 4, point.Y - 4, 8, 8));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_image is null || _cropMode == CropMode.None) return;
        Focus(); CaptureMouse();
        _start = ToImage(e.GetPosition(this));
        _startSelection = _selection;
        _drag = HitTestDrag(e.GetPosition(this));
        if (_drag == DragKind.None) _drag = DragKind.Draw;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_image is null || _drag == DragKind.None || e.LeftButton != MouseButtonState.Pressed) return;
        var current = ToImage(e.GetPosition(this));
        if (_drag == DragKind.Move)
        {
            _selection = CropGeometry.Move(_startSelection, (int)Math.Round(current.X - _start.X), (int)Math.Round(current.Y - _start.Y), _image.PixelWidth, _image.PixelHeight);
        }
        else if (_drag == DragKind.Draw)
        {
            _selection = CropGeometry.FromDrag(_start, current, _cropMode, _image.PixelWidth, _image.PixelHeight);
        }
        else
        {
            var left = _startSelection.X;
            var top = _startSelection.Y;
            var right = _startSelection.X + _startSelection.Width;
            var bottom = _startSelection.Y + _startSelection.Height;
            if (_drag is DragKind.Left or DragKind.TopLeft or DragKind.BottomLeft) left = (int)Math.Round(current.X);
            if (_drag is DragKind.Right or DragKind.TopRight or DragKind.BottomRight) right = (int)Math.Round(current.X);
            if (_drag is DragKind.Top or DragKind.TopLeft or DragKind.TopRight) top = (int)Math.Round(current.Y);
            if (_drag is DragKind.Bottom or DragKind.BottomLeft or DragKind.BottomRight) bottom = (int)Math.Round(current.Y);
            _selection = CropGeometry.Clamp(new Int32Rect(Math.Min(left, right), Math.Min(top, bottom), Math.Max(1, Math.Abs(right - left)), Math.Max(1, Math.Abs(bottom - top))), _cropMode, _image.PixelWidth, _image.PixelHeight);
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _drag = DragKind.None;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private DragKind HitTestDrag(Point point)
    {
        if (_selection.IsEmpty) return DragKind.None;
        var rect = ToScreen(_selection);
        const double h = 8;
        if (Near(point, rect.TopLeft, h)) return DragKind.TopLeft;
        if (Near(point, rect.TopRight, h)) return DragKind.TopRight;
        if (Near(point, rect.BottomLeft, h)) return DragKind.BottomLeft;
        if (Near(point, rect.BottomRight, h)) return DragKind.BottomRight;
        if (Math.Abs(point.X - rect.Left) <= h && point.Y >= rect.Top && point.Y <= rect.Bottom) return DragKind.Left;
        if (Math.Abs(point.X - rect.Right) <= h && point.Y >= rect.Top && point.Y <= rect.Bottom) return DragKind.Right;
        if (Math.Abs(point.Y - rect.Top) <= h && point.X >= rect.Left && point.X <= rect.Right) return DragKind.Top;
        if (Math.Abs(point.Y - rect.Bottom) <= h && point.X >= rect.Left && point.X <= rect.Right) return DragKind.Bottom;
        return rect.Contains(point) ? DragKind.Move : DragKind.None;
    }

    private Rect ToScreen(Int32Rect rect) => new(rect.X * Zoom, rect.Y * Zoom, rect.Width * Zoom, rect.Height * Zoom);
    private Point ToImage(Point point) => new(Math.Clamp(point.X / Zoom, 0, _image!.PixelWidth), Math.Clamp(point.Y / Zoom, 0, _image.PixelHeight));
    private static bool Near(Point a, Point b, double distance) => Math.Abs(a.X - b.X) <= distance && Math.Abs(a.Y - b.Y) <= distance;
    private static IEnumerable<Point> HandlePoints(Rect r)
    {
        yield return r.TopLeft; yield return new Point(r.Left + r.Width / 2, r.Top); yield return r.TopRight;
        yield return new Point(r.Left, r.Top + r.Height / 2); yield return new Point(r.Right, r.Top + r.Height / 2);
        yield return r.BottomLeft; yield return new Point(r.Left + r.Width / 2, r.Bottom); yield return r.BottomRight;
    }
}
