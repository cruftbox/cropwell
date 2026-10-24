using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Cropwell.Dialogs;
using Cropwell.Models;
using Cropwell.Providers;
using Cropwell.Services;

namespace Cropwell;

public partial class MainWindow : Window
{
    private const string Instruction = "You write accessible alt text. Return one plain sentence that describes what a person needs to see in the image.";
    private readonly SettingsService _settingsService = new();
    private AppSettings _settings;
    private ImageDocument? _document;
    private string? _lastExportFolder;
    private AltTextWindow? _altTextWindow;
    private bool _spaceDown;
    private bool _panning;
    private Point _panStart;
    private double _panHorizontal;
    private double _panVertical;

    public string? InitialPath { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        Canvas.SelectionChanged += (_, _) => UpdateStatus();
        ApplyWindowSettings();
        UpdateUi();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(InitialPath) && File.Exists(InitialPath)) await OpenPathAsync(InitialPath);
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog
        {
            Title = "Open image",
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.webp;*.gif;*.heic;*.heif;*.tif;*.tiff;*.bmp|All files|*.*",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true) await OpenPathAsync(dialog.FileName);
    }

    private async Task OpenPathAsync(string path)
    {
        try
        {
            var image = await Task.Run(() => ImageLoader.LoadNormalized(path));
            _document = new ImageDocument(image, path);
            Canvas.Image = image;
            Canvas.CropMode = CropMode.None;
            EmptyMessage.Visibility = Visibility.Collapsed;
            FitToWindow();
            AppLog.Info($"Opened {Path.GetFileName(path)}");
            UpdateUi();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not open {Path.GetFileName(path)}", ex);
            MessageBox.Show(this, "Cropwell could not open that image.", "Open image", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e) => ExportCurrent();

    private bool ExportCurrent()
    {
        if (_document is null) return false;
        var sourceName = Path.GetFileNameWithoutExtension(_document.SourcePath);
        var dialog = new SaveFileDialog
        {
            Title = "Export image",
            FileName = sourceName + "-cropwell.png",
            InitialDirectory = ResolveInitialExportFolder(_settings, _document.SourcePath, _lastExportFolder),
            Filter = "PNG image|*.png|WebP image|*.webp|JPEG image|*.jpg|GIF image|*.gif",
            FilterIndex = 1,
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return false;
        var format = dialog.FilterIndex switch { 2 => ExportFormat.WebP, 3 => ExportFormat.Jpeg, 4 => ExportFormat.Gif, _ => ExportFormat.Png };
        var destination = Path.ChangeExtension(dialog.FileName, ImageExporter.Extension(format));
        try
        {
            ImageExporter.Export(_document.Image, destination, _document.SourcePath, format);
            _document.MarkExported();
            _lastExportFolder = Path.GetDirectoryName(destination);
            AppLog.Info($"Exported {Path.GetFileName(destination)}");
            UpdateUi();
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not export {Path.GetFileName(destination)}", ex);
            MessageBox.Show(this, ex is InvalidOperationException ? ex.Message : "Cropwell could not export the image.", "Export image", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    internal static string? ResolveInitialExportFolder(AppSettings settings, string sourcePath, string? lastExportFolder)
    {
        if (!string.IsNullOrWhiteSpace(settings.ExportFolder) && Directory.Exists(settings.ExportFolder))
            return settings.ExportFolder;
        return lastExportFolder ?? Path.GetDirectoryName(sourcePath);
    }

    private void Undo_Click(object sender, RoutedEventArgs e) { _document?.Undo(); DocumentChanged(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { _document?.Redo(); DocumentChanged(); }

    private void Resize_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        var dialog = new ResizeDialog(_document.Image.PixelWidth, _document.Image.PixelHeight) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _document.Resize(dialog.ResultWidth, dialog.ResultHeight);
            DocumentChanged();
            FitToWindow();
        }
    }

    private void SquareCrop_Click(object sender, RoutedEventArgs e) => BeginCrop(CropMode.Square);
    private void PortraitCrop_Click(object sender, RoutedEventArgs e) => BeginCrop(CropMode.Portrait);
    private void FreeCrop_Click(object sender, RoutedEventArgs e) => BeginCrop(CropMode.Free);
    private void BeginCrop(CropMode mode) { if (_document is not null) Canvas.CropMode = mode; UpdateUi(); }

    private void ApplyCrop_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null || Canvas.Selection.IsEmpty) return;
        _document.Crop(Canvas.Selection);
        Canvas.ClearSelection();
        DocumentChanged();
        FitToWindow();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(Canvas.Zoom * 1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(Canvas.Zoom / 1.25);
    private void Fit_Click(object sender, RoutedEventArgs e) => FitToWindow();
    private void Actual_Click(object sender, RoutedEventArgs e) => SetZoom(1);

    private void SetZoom(double zoom)
    {
        Canvas.Zoom = zoom;
        UpdateStatus();
    }

    private void FitToWindow()
    {
        if (_document is null || CanvasScroller.ViewportWidth <= 0 || CanvasScroller.ViewportHeight <= 0) return;
        var zoom = Math.Min((CanvasScroller.ViewportWidth - 20) / _document.Image.PixelWidth, (CanvasScroller.ViewportHeight - 20) / _document.Image.PixelHeight);
        SetZoom(Math.Clamp(zoom, .1, 8));
    }

    private async void AltText_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        if (_altTextWindow is null)
        {
            _altTextWindow = new AltTextWindow { Owner = this };
            _altTextWindow.Closed += (_, _) => _altTextWindow = null;
        }
        _altTextWindow.Show();
        _altTextWindow.Activate();
        await GenerateAltTextAsync();
    }

    internal async Task GenerateAltTextAsync()
    {
        if (_document is null || _altTextWindow is null) return;
        var id = _settings.ActiveProvider;
        var configuration = _settings.Providers[id];
        var (key, _) = CredentialStore.Resolve(id, configuration);
        IAltTextProvider provider;
        try { provider = ProviderFactory.Create(id, configuration, key); }
        catch (ProviderException ex) { _altTextWindow.ShowError(ex.Message); return; }

        var revision = _document.Revision;
        AltTextButton.IsEnabled = false;
        var cancellation = _altTextWindow.BeginBusy(provider.DisplayName, provider.Model);
        try
        {
            var encoded = await Task.Run(() => ApiImageEncoder.Encode(_document.Image), cancellation.Token);
            AppLog.Info($"Alt text request: provider={provider.DisplayName}, model={provider.Model}");
            var text = await provider.AltTextAsync(encoded.MediaType, encoded.Base64, Instruction, cancellation.Token);
            if (!cancellation.IsCancellationRequested) _altTextWindow.ShowResult(text, provider.DisplayName, provider.Model, _document.Revision != revision);
        }
        catch (OperationCanceledException) { _altTextWindow.ShowCancelled(); }
        catch (ProviderException ex) { AppLog.Error($"Provider failure: {provider.DisplayName} {provider.Model}", ex); _altTextWindow.ShowError(ex.Message); }
        catch (Exception ex) { AppLog.Error("Alt text generation failed", ex); _altTextWindow.ShowError("Cropwell could not prepare or send the image."); }
        finally { AltTextButton.IsEnabled = true; }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings = dialog.Result;
            _settingsService.Save(_settings);
        }
    }

    private void DocumentChanged()
    {
        if (_document is null) return;
        Canvas.Image = _document.Image;
        _altTextWindow?.MarkPossiblyOutOfDate();
        UpdateUi();
    }

    private void UpdateUi()
    {
        var hasDocument = _document is not null;
        ExportButton.IsEnabled = hasDocument;
        ResizeButton.IsEnabled = hasDocument;
        UndoButton.IsEnabled = _document?.CanUndo == true;
        RedoButton.IsEnabled = _document?.CanRedo == true;
        SquareCropButton.IsEnabled = hasDocument;
        PortraitCropButton.IsEnabled = hasDocument;
        FreeCropButton.IsEnabled = hasDocument;
        ZoomOutButton.IsEnabled = hasDocument;
        ZoomInButton.IsEnabled = hasDocument;
        FitButton.IsEnabled = hasDocument;
        ActualButton.IsEnabled = hasDocument;
        AltTextButton.IsEnabled = hasDocument;
        ApplyCropButton.IsEnabled = !Canvas.Selection.IsEmpty;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        DimensionsText.Text = _document is null ? "No image" : $"{_document.Image.PixelWidth} × {_document.Image.PixelHeight} px";
        ZoomText.Text = $"{Canvas.Zoom:P0}";
        SelectionText.Text = Canvas.Selection.IsEmpty ? "" : $"Selection: {Canvas.Selection.Width} × {Canvas.Selection.Height} px";
        ApplyCropButton.IsEnabled = !Canvas.Selection.IsEmpty;
        SquareCropButton.IsChecked = Canvas.CropMode == CropMode.Square;
        PortraitCropButton.IsChecked = Canvas.CropMode == CropMode.Portrait;
        FreeCropButton.IsChecked = Canvas.CropMode == CropMode.Free;
    }

    private bool ConfirmDiscard()
    {
        if (_document?.HasUnexportedEdits != true) return true;
        var dialog = new DiscardDialog { Owner = this };
        var result = dialog.ShowDialog();
        if (result != true) return false;
        return dialog.Choice != DiscardChoice.Export || ExportCurrent();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmDiscard()) { e.Cancel = true; return; }
        _settings.Window = new WindowSettings { Left = Left, Top = Top, Width = Width, Height = Height };
        try { _settingsService.Save(_settings); } catch (Exception ex) { AppLog.Warning("Window settings could not be saved.", ex); }
    }

    private void ApplyWindowSettings()
    {
        Width = Math.Max(MinWidth, _settings.Window.Width);
        Height = Math.Max(MinHeight, _settings.Window.Height);
        if (_settings.Window.Left is double left && _settings.Window.Top is double top)
        { Left = left; Top = top; WindowStartupLocation = WindowStartupLocation.Manual; }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files is not { Length: 1 } || !ConfirmDiscard()) return;
        await OpenPathAsync(files[0]);
    }

    private void OnDragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) _spaceDown = true;
        if (e.Key == Key.Escape && !Canvas.Selection.IsEmpty) { Canvas.ClearSelection(); e.Handled = true; }
        else if (e.Key == Key.Enter && !Canvas.Selection.IsEmpty) { ApplyCrop_Click(sender, e); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.O: Open_Click(sender, e); break; case Key.E: Export_Click(sender, e); break;
                case Key.Z: Undo_Click(sender, e); break; case Key.Y: Redo_Click(sender, e); break;
                case Key.R: Resize_Click(sender, e); break; case Key.G: AltText_Click(sender, e); break;
                case Key.OemPlus: case Key.Add: ZoomIn_Click(sender, e); break;
                case Key.OemMinus: case Key.Subtract: ZoomOut_Click(sender, e); break;
                case Key.D0: case Key.NumPad0: Fit_Click(sender, e); break; case Key.D1: case Key.NumPad1: Actual_Click(sender, e); break;
                case Key.OemComma: Settings_Click(sender, e); break; default: return;
            }
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z) { Redo_Click(sender, e); e.Handled = true; }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e) { if (e.Key == Key.Space) _spaceDown = false; }

    private void CanvasScroller_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { SetZoom(Canvas.Zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1)); e.Handled = true; }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) { CanvasScroller.ScrollToHorizontalOffset(CanvasScroller.HorizontalOffset - e.Delta); e.Handled = true; }
    }

    private void CanvasScroller_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle && !(_spaceDown && e.ChangedButton == MouseButton.Left)) return;
        _panning = true; _panStart = e.GetPosition(CanvasScroller); _panHorizontal = CanvasScroller.HorizontalOffset; _panVertical = CanvasScroller.VerticalOffset;
        CanvasScroller.CaptureMouse(); e.Handled = true;
    }
    private void CanvasScroller_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var point = e.GetPosition(CanvasScroller);
        CanvasScroller.ScrollToHorizontalOffset(_panHorizontal - (point.X - _panStart.X));
        CanvasScroller.ScrollToVerticalOffset(_panVertical - (point.Y - _panStart.Y));
        e.Handled = true;
    }
    private void CanvasScroller_OnPreviewMouseUp(object sender, MouseButtonEventArgs e) { if (_panning) { _panning = false; CanvasScroller.ReleaseMouseCapture(); e.Handled = true; } }
}
