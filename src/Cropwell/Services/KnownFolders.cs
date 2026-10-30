using System.Runtime.InteropServices;

namespace Cropwell.Services;

public static class KnownFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    // .NET has no SpecialFolder value for Downloads, so the shell is asked directly.
    public static string Downloads()
    {
        var result = SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var pointer);
        try
        {
            if (result == 0 && Marshal.PtrToStringUni(pointer) is { Length: > 0 } path) return path;
        }
        finally { Marshal.FreeCoTaskMem(pointer); }
        return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
