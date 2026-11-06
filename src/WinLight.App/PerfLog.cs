using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace WinLight.App;

internal static class PerfLog
{
    private const long MaxBytes = 1024 * 1024;
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("WINLIGHT_PERF") == "1";
    private static readonly BlockingCollection<string>? Lines = Enabled ? new BlockingCollection<string>() : null;
    private static readonly DateTime? ProcessStartUtc = Enabled ? Process.GetCurrentProcess().StartTime.ToUniversalTime() : null;

    static PerfLog()
    {
        if (!Enabled) return;
        var writer = new Thread(WriteLoop) { IsBackground = true, Name = "WinLight performance log" };
        writer.Start();
    }

    public static long Stamp() => Enabled ? Stopwatch.GetTimestamp() : 0;

    public static void Record(string metric, long start, string detail = "")
    {
        if (!Enabled || start == 0) return;
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Write(metric, milliseconds, detail);
    }

    public static void RecordStartup(string milestone, string detail = "")
    {
        if (!Enabled || ProcessStartUtc is null) return;
        double milliseconds = (DateTime.UtcNow - ProcessStartUtc.Value).TotalMilliseconds;
        Write(milestone, milliseconds, detail);
    }

    private static void Write(string metric, double milliseconds, string detail)
    {
        Lines!.Add(string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.UtcNow:O}|{metric}|{milliseconds:F3}|{detail}"));
    }

    private static void WriteLoop()
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinLight", "logs");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "perf.log");
            StreamWriter writer = Open(path);
            try
            {
                foreach (string line in Lines!.GetConsumingEnumerable())
                {
                    if (writer.BaseStream.Length >= MaxBytes)
                    {
                        writer.Dispose();
                        File.Move(path, path + ".old", overwrite: true);
                        writer = Open(path);
                    }
                    writer.WriteLine(line);
                }
            }
            finally { writer.Dispose(); }
        }
        catch (Exception error) { AppLog.Error("Write performance log", error); }
    }

    private static StreamWriter Open(string path) => new(path, append: true) { AutoFlush = true };
}
