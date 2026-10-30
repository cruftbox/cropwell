using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Windows.Controls;
using Cropwell.Controls;
using Cropwell.Dialogs;
using Cropwell.Models;
using Cropwell.Providers;
using Cropwell.Services;

namespace Cropwell.Tests;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    [STAThread]
    private static int Main()
    {
        Run("Logging initialization is non-fatal", AppLog.Initialize);
        Run("Main window constructs", () =>
        {
            var window = new MainWindow();
            window.Close();
        });
        Run("Image canvas is centered", () =>
        {
            var window = new MainWindow();
            var canvas = (ImageCanvas)window.FindName("Canvas");
            var scroller = (ScrollViewer)window.FindName("CanvasScroller");
            Equal(HorizontalAlignment.Center, canvas.HorizontalAlignment);
            Equal(VerticalAlignment.Center, canvas.VerticalAlignment);
            window.ShowInTaskbar = false;
            window.Opacity = 0;
            window.Show();
            canvas.Image = Solid(100, 80, Colors.CornflowerBlue);
            window.UpdateLayout();
            var origin = canvas.TranslatePoint(new Point(0, 0), scroller);
            if (origin.X <= 0 || origin.Y <= 0)
                throw new Exception($"origin={origin}, canvas={canvas.ActualWidth}x{canvas.ActualHeight}, desired={canvas.DesiredSize}, viewport={scroller.ViewportWidth}x{scroller.ViewportHeight}");
            window.Close();
        });
        Run("Square selection is exact", () =>
        {
            var r = CropGeometry.InitialSelection(CropMode.Square, 1200, 800);
            Equal(800, r.Width); Equal(800, r.Height); Equal(200, r.X);
        });
        Run("Portrait selection uses whole-pixel ratio", () =>
        {
            var r = CropGeometry.InitialSelection(CropMode.Portrait, 1200, 800);
            Equal(9 * 50, r.Width); Equal(16 * 50, r.Height);
        });
        Run("Crop movement stays within bounds", () =>
        {
            var r = CropGeometry.Move(new Int32Rect(10, 10, 80, 60), -100, 100, 100, 100);
            Equal(new Int32Rect(0, 40, 80, 60), r);
        });
        Run("Free crop enforces minimum size", () =>
        {
            var r = CropGeometry.Clamp(new Int32Rect(3, 4, 2, 2), CropMode.Free, 100, 100);
            Equal(16, r.Width); Equal(16, r.Height);
        });
        Run("Resize fills aspect and rounds", () =>
        {
            Equal((1000, 563), ImageTransforms.AspectSize(1920, 1080, 1000, null));
            Equal((1001, 563), ImageTransforms.AspectSize(1920, 1080, null, 563));
        });
        Run("Lanczos resize produces exact dimensions", () =>
        {
            var source = Solid(31, 17, Colors.CornflowerBlue);
            var result = ImageTransforms.ResizeLanczos(source, 13, 7);
            Equal(13, result.PixelWidth); Equal(7, result.PixelHeight);
        });
        Run("Animated GIF normalization uses first frame", TestFirstGifFrame);
        Run("EXIF orientation is applied", TestExifOrientation);
        Run("Worker-thread image can be cropped on UI thread", TestWorkerThreadLoadCanCrop);
        Run("API encoding caps initial long edge", () =>
        {
            var encoded = ApiImageEncoder.Encode(Solid(3000, 1000, Colors.DarkSeaGreen));
            Equal(2048, encoded.Width); Equal(true, encoded.Base64.Length <= ApiImageEncoder.MaximumBase64Characters);
        });
        Run("API encoding stops below 512 pixels", () =>
        {
            Throws<InvalidOperationException>(() => ApiImageEncoder.Encode(Solid(513, 513, Colors.DarkSeaGreen), 1));
        });
        Run("JPG flattening uses white", () =>
        {
            var pixel = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 0 }, 4);
            var flat = ApiImageEncoder.FlattenWhite(pixel); var bytes = new byte[4]; flat.CopyPixels(bytes, 4, 0);
            Equal((byte)255, bytes[0]); Equal((byte)255, bytes[1]); Equal((byte)255, bytes[2]); Equal((byte)255, bytes[3]);
        });
        Run("JPG export flattens transparency", TestJpegExportFlattens);
        Run("Export strips metadata", TestExportStripsMetadata);
        Run("Export refuses source path", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            File.WriteAllBytes(path, [1]);
            try { Throws<InvalidOperationException>(() => ImageExporter.Export(Solid(2, 2, Colors.Red), path, path, ExportFormat.Png)); }
            finally { File.Delete(path); }
        });
        Run("Environment key is fallback", () =>
        {
            var id = "cropwell-test-" + Guid.NewGuid();
            var variable = "CROPWELL_TEST_KEY_" + Guid.NewGuid().ToString("N");
            Environment.SetEnvironmentVariable(variable, "test-secret");
            try
            {
                var result = CredentialStore.Resolve(id, new ProviderSettings { EnvironmentVariable = variable });
                Equal("test-secret", result.Key!); Equal($"environment variable ({variable})", result.Source);
            }
            finally { Environment.SetEnvironmentVariable(variable, null); }
        });
        Run("Stored key takes precedence", () =>
        {
            var result = CredentialStore.ChooseSource("stored-secret", "environment-secret", "TEST_KEY");
            Equal("stored-secret", result.Key!); Equal("stored key", result.Source);
        });
        Run("All four real providers have independent settings", () =>
        {
            var providers = ProviderDefaults.Create();
            Equal(true, providers.ContainsKey("anthropic"));
            Equal(true, providers.ContainsKey("openai"));
            Equal(true, providers.ContainsKey("gemini"));
            Equal(true, providers.ContainsKey("muse"));
            Equal(4, providers.Count(x => x.Key != "mock"));
            Equal(4, providers.Values.Select(x => x.EnvironmentVariable).Distinct().Count());
            Equal("https://api.anthropic.com", providers["anthropic"].BaseUrl);
            Equal("https://api.openai.com", providers["openai"].BaseUrl);
            Equal("https://generativelanguage.googleapis.com", providers["gemini"].BaseUrl);
            Equal("low", providers["gemini"].ReasoningEffort);
            Equal("https://api.meta.ai/v1", providers["muse"].BaseUrl);
        });
        Run("Responses endpoint does not duplicate v1", () =>
        {
            Equal("https://api.openai.com/v1/responses", OpenAiResponsesProvider.BuildResponsesUri("https://api.openai.com").AbsoluteUri);
            Equal("https://api.meta.ai/v1/responses", OpenAiResponsesProvider.BuildResponsesUri("https://api.meta.ai/v1").AbsoluteUri);
        });
        Run("Mock setting is removed unless enabled", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "cropwell-test-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(folder, "settings.json");
            try
            {
                var service = new SettingsService(path);
                var settings = new AppSettings();
                settings.Providers["mock"] = new ProviderSettings { DisplayName = "Mock", Type = "mock", Model = "mock-v1" };
                service.Save(settings);
                Environment.SetEnvironmentVariable("CROPWELL_MOCK", null);
                Equal(false, service.Load().Providers.ContainsKey("mock"));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        });
        Run("Per-provider settings round trip independently", TestSettingsRoundTrip);
        Run("Configured export folder takes priority", TestConfiguredExportFolder);
        Run("Capture exports to configured folder or Downloads", TestCaptureExportFolder);
        Run("Capture selection maps view units to pixels", () =>
        {
            var view = new Size(1000, 500);
            Equal(new Int32Rect(15, 30, 150, 300), CaptureOverlay.ToPixels(new Point(10, 20), new Point(110, 220), view, 1500, 750));
            Equal(new Int32Rect(15, 30, 150, 300), CaptureOverlay.ToPixels(new Point(110, 220), new Point(10, 20), view, 1500, 750));
            Equal(new Int32Rect(1350, 0, 150, 30), CaptureOverlay.ToPixels(new Point(900, 20), new Point(2000, -50), view, 1500, 750));
        });
        Run("Tiny capture drags are ignored", () =>
        {
            Equal(true, CaptureOverlay.IsTooSmall(new Int32Rect(10, 10, 4, 300)));
            Equal(true, CaptureOverlay.IsTooSmall(new Int32Rect(10, 10, 300, 0)));
            Equal(false, CaptureOverlay.IsTooSmall(new Int32Rect(10, 10, 5, 5)));
        });
        Run("Captured pixels are made opaque", () =>
        {
            var pixel = BitmapSource.Create(1, 1, 120, 120, PixelFormats.Bgr32, null, new byte[] { 10, 20, 30, 0 }, 4);
            var opaque = ScreenCapture.ToOpaqueBgra(pixel); var bytes = new byte[4]; opaque.CopyPixels(bytes, 4, 0);
            Equal(PixelFormats.Bgra32, opaque.Format); Equal(96.0, opaque.DpiX);
            Equal((byte)10, bytes[0]); Equal((byte)20, bytes[1]); Equal((byte)30, bytes[2]); Equal((byte)255, bytes[3]);
        });
        Run("Capture document has no source and a timestamped name", () =>
        {
            var document = ImageDocument.FromCapture(Solid(2, 2, Colors.Red), new DateTime(2026, 10, 30, 14, 30, 12));
            Equal(true, document.SourcePath is null);
            Equal("Capture 2026-10-30 143012.png", document.ExportFileName);
            Equal("photo-cropwell.png", new ImageDocument(Solid(2, 2, Colors.Red), @"C:\images\photo.jpg").ExportFileName);
        });
        Run("Export without a source path succeeds", () =>
        {
            var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            try { ImageExporter.Export(Solid(2, 2, Colors.Red), destination, null, ExportFormat.Png); Equal(true, File.Exists(destination)); }
            finally { if (File.Exists(destination)) File.Delete(destination); }
        });
        Run("Settings displays stored export folder", TestSettingsDisplaysExportFolder);
        Run("Mock provider honors cancellation", () =>
        {
            var provider = new MockProvider(new ProviderSettings { DisplayName = "Mock", Model = "mock-v1" });
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                provider.AltTextAsync("image/png", "", "", cancellation.Token).GetAwaiter().GetResult();
                throw new Exception("expected cancellation");
            }
            catch (OperationCanceledException) { }
        });
        Run("Provider validation uses image-capable flow", () =>
        {
            var provider = new MockProvider(new ProviderSettings { DisplayName = "Mock", Model = "mock-v1" });
            ProviderValidator.ValidateAsync(provider, CancellationToken.None).GetAwaiter().GetResult();
        });

        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static void TestFirstGifFrame()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        try
        {
            var encoder = new GifBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Solid(3, 2, Colors.Red)));
            encoder.Frames.Add(BitmapFrame.Create(Solid(5, 4, Colors.Blue)));
            using (var stream = File.Create(path)) encoder.Save(stream);
            var loaded = ImageLoader.LoadNormalized(path);
            Equal(3, loaded.PixelWidth); Equal(2, loaded.PixelHeight);
        }
        finally { File.Delete(path); }
    }

    private static void TestExifOrientation()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
        try
        {
            var metadata = new BitmapMetadata("jpg");
            metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
            var encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Solid(3, 2, Colors.Red), null, metadata, null));
            using (var stream = File.Create(path)) encoder.Save(stream);
            var loaded = ImageLoader.LoadNormalized(path);
            Equal(2, loaded.PixelWidth); Equal(3, loaded.PixelHeight);
        }
        finally { File.Delete(path); }
    }

    private static void TestWorkerThreadLoadCanCrop()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
        try
        {
            var encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Solid(20, 12, Colors.CornflowerBlue)));
            using (var stream = File.Create(path)) encoder.Save(stream);
            var loaded = Task.Run(() => ImageLoader.LoadNormalized(path)).GetAwaiter().GetResult();
            var cropped = ImageTransforms.Crop(loaded, new Int32Rect(2, 2, 10, 8));
            Equal(10, cropped.PixelWidth); Equal(8, cropped.PixelHeight);
        }
        finally { File.Delete(path); }
    }

    private static void TestJpegExportFlattens()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
        try
        {
            var transparent = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, new byte[8 * 8 * 4], 8 * 4);
            ImageExporter.Export(transparent, destination, sourcePath, ExportFormat.Jpeg);
            var decoded = ImageLoader.LoadNormalized(destination);
            var pixel = new byte[4]; decoded.CopyPixels(new Int32Rect(4, 4, 1, 1), pixel, 4, 0);
            Equal(true, pixel[0] >= 250 && pixel[1] >= 250 && pixel[2] >= 250);
        }
        finally { if (File.Exists(destination)) File.Delete(destination); }
    }

    private static void TestExportStripsMetadata()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            var metadata = new BitmapMetadata("png");
            metadata.SetQuery("/tEXt/{str=Comment}", "private metadata");
            var frame = BitmapFrame.Create(Solid(4, 4, Colors.Blue), null, metadata, null);
            ImageExporter.Export(frame, destination, sourcePath, ExportFormat.Png);
            using var stream = File.OpenRead(destination);
            var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var outputMetadata = decoded.Metadata as BitmapMetadata;
            Equal(true, outputMetadata?.GetQuery("/tEXt/{str=Comment}") is null);
        }
        finally { if (File.Exists(destination)) File.Delete(destination); }
    }

    private static void TestSettingsRoundTrip()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cropwell-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "settings.json");
        try
        {
            var service = new SettingsService(path);
            var settings = new AppSettings { ActiveProvider = "gemini", ExportFolder = folder };
            settings.Providers["anthropic"].Model = "claude-test";
            settings.Providers["anthropic"].BaseUrl = "";
            settings.Providers["gemini"].ReasoningEffort = "minimal";
            settings.Providers["muse"].BaseUrl = "https://example.test/v1";
            service.Save(settings);
            var loaded = service.Load();
            Equal("gemini", loaded.ActiveProvider);
            Equal(folder, loaded.ExportFolder);
            Equal("claude-test", loaded.Providers["anthropic"].Model);
            Equal("https://api.anthropic.com", loaded.Providers["anthropic"].BaseUrl);
            Equal("low", loaded.Providers["gemini"].ReasoningEffort);
            Equal("https://example.test/v1", loaded.Providers["muse"].BaseUrl);
            Equal("gpt-6-sol", loaded.Providers["openai"].Model);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private static void TestConfiguredExportFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "cropwell-test-" + Guid.NewGuid().ToString("N"));
        var configured = Path.Combine(root, "configured");
        var source = Path.Combine(root, "source", "image.png");
        var previous = Path.Combine(root, "previous");
        Directory.CreateDirectory(configured);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        try
        {
            Equal(configured, MainWindow.ResolveInitialExportFolder(new AppSettings { ExportFolder = configured }, source, previous)!);
            Equal(previous, MainWindow.ResolveInitialExportFolder(new AppSettings(), source, previous)!);
            Equal(Path.GetDirectoryName(source)!, MainWindow.ResolveInitialExportFolder(new AppSettings(), source, null)!);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void TestCaptureExportFolder()
    {
        var configured = Path.Combine(Path.GetTempPath(), "cropwell-test-" + Guid.NewGuid().ToString("N"));
        var previous = Path.Combine(Path.GetTempPath(), "previous");
        Directory.CreateDirectory(configured);
        try
        {
            Equal(configured, MainWindow.ResolveInitialExportFolder(new AppSettings { ExportFolder = configured }, null, previous)!);
            Equal(KnownFolders.Downloads(), MainWindow.ResolveInitialExportFolder(new AppSettings(), null, previous)!);
            Equal(true, Path.IsPathRooted(KnownFolders.Downloads()));
        }
        finally { Directory.Delete(configured, true); }
    }

    private static void TestSettingsDisplaysExportFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cropwell-export-folder");
        var dialog = new Cropwell.Dialogs.SettingsDialog(new AppSettings { ExportFolder = folder });
        try
        {
            var exportFolderBox = (TextBox)dialog.FindName("ExportFolderBox");
            Equal(folder, exportFolderBox.Text);
        }
        finally { dialog.Close(); }
    }

    private static BitmapSource Solid(int width, int height, Color color)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = color.A; }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); bitmap.Freeze(); return bitmap;
    }
    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); _passed++; }
        catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex); _failed++; }
    }
    private static void Equal<T>(T expected, T actual) where T : notnull { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"expected {expected}, got {actual}"); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"expected {typeof(T).Name}"); }
}
