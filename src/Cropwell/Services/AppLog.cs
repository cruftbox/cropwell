using System.Reflection;

namespace Cropwell.Services;

public static class AppLog
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cropwell", "logs");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "cropwell.log");

    public static void Initialize()
    {
        Info($"Cropwell {Assembly.GetExecutingAssembly().GetName().Version} starting");
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warning(string message, Exception? exception = null) => Write("WARN", message, exception);
    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                RotateIfNeeded();
                var safe = message.Replace(Environment.NewLine, " ");
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {level} {safe}{(exception is null ? "" : " | " + exception)}{Environment.NewLine}");
            }
        }
        catch { }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(FilePath) || new FileInfo(FilePath).Length < MaxBytes) return;
        for (var i = 3; i >= 1; i--)
        {
            var source = i == 1 ? FilePath : FilePath + "." + (i - 1);
            var destination = FilePath + "." + i;
            if (File.Exists(source)) File.Move(source, destination, true);
        }
    }
}
