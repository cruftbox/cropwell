using System.Globalization;
using System.Windows;
using System.Windows.Media.Imaging;
using Cropwell.Services;

namespace Cropwell.Models;

public sealed class ImageDocument
{
    private const long HistoryLimitBytes = 1_500_000_000;
    private readonly List<BitmapSource> _undo = [];
    private readonly List<BitmapSource> _redo = [];

    public BitmapSource Image { get; private set; }
    /// <summary>The opened file, or null for a screen capture.</summary>
    public string? SourcePath { get; }
    public string ExportFileName { get; }
    public int Revision { get; private set; }
    public int ExportedRevision { get; private set; }
    public bool HasUnexportedEdits => Revision > 0 && Revision != ExportedRevision;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public ImageDocument(BitmapSource image, string sourcePath)
        : this(image, Path.GetFullPath(sourcePath), Path.GetFileNameWithoutExtension(sourcePath) + "-cropwell.png") { }

    private ImageDocument(BitmapSource image, string? sourcePath, string exportFileName)
    {
        Image = image;
        SourcePath = sourcePath;
        ExportFileName = exportFileName;
    }

    public static ImageDocument FromCapture(BitmapSource image, DateTime capturedAt) =>
        new(image, null, $"Capture {capturedAt.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture)}.png");

    public void Crop(Int32Rect selection)
    {
        selection = CropGeometry.Clamp(selection, CropMode.Free, Image.PixelWidth, Image.PixelHeight);
        Apply(ImageTransforms.Crop(Image, selection));
    }

    public void Resize(int width, int height) => Apply(ImageTransforms.ResizeLanczos(Image, width, height));

    public void Undo()
    {
        if (!CanUndo) return;
        _redo.Add(Image);
        Image = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Revision++;
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _undo.Add(Image);
        Image = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Revision++;
        TrimHistory();
    }

    public void MarkExported() => ExportedRevision = Revision;

    private void Apply(BitmapSource result)
    {
        _undo.Add(Image);
        _redo.Clear();
        Image = result;
        Revision++;
        TrimHistory();
    }

    private void TrimHistory()
    {
        long bytes = _undo.Sum(EstimateBytes) + _redo.Sum(EstimateBytes);
        while (_undo.Count > 0 && bytes > HistoryLimitBytes)
        {
            bytes -= EstimateBytes(_undo[0]);
            _undo.RemoveAt(0);
        }
    }

    private static long EstimateBytes(BitmapSource bitmap) =>
        (long)bitmap.PixelWidth * bitmap.PixelHeight * Math.Max(4, (bitmap.Format.BitsPerPixel + 7) / 8);
}
