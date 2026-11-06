namespace WinLight.App;

internal static class DiagnosticLog
{
    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("WINLIGHT_DIAGNOSTICS") == "1";
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinLight", "logs");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "diagnostics.log");
                if (File.Exists(path) && new FileInfo(path).Length >= MaxBytes)
                    File.Move(path, path + ".old", overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch (Exception error) { AppLog.Error("Diagnostic logging", error); }
    }
}
