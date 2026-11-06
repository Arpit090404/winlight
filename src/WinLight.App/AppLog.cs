using System.Diagnostics;
using System.Security;

namespace WinLight.App;

internal static class AppLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static void Error(string operation, Exception error)
    {
        try
        {
            lock (Gate)
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WinLight", "logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "WinLight.log");
                if (File.Exists(path) && new FileInfo(path).Length >= MaxBytes)
                    File.Move(path, path + ".old", overwrite: true);

                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {operation}: {error}{Environment.NewLine}");
            }
        }
        catch (Exception loggingError) when (loggingError is IOException or UnauthorizedAccessException or SecurityException)
        {
            Trace.WriteLine($"WinLight could not write its log: {loggingError}; original error: {error}");
        }
    }
}
