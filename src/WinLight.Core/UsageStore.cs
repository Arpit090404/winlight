using System.Text.Json;

namespace WinLight.Core;

public static class UsageStore
{
    private static readonly SemaphoreSlim WriteGate = new(1, 1);

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinLight", "settings.json");

    public static async Task<Dictionary<string, UsageRecord>> LoadAsync(string path, CancellationToken stop = default)
    {
        if (!File.Exists(path)) return new(StringComparer.OrdinalIgnoreCase);
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, UsageRecord>>(stream, cancellationToken: stop)
            .ConfigureAwait(false) ?? new(StringComparer.OrdinalIgnoreCase);
    }

    public static async Task SaveAsync(string path, IReadOnlyDictionary<string, UsageRecord> usage, CancellationToken stop = default)
    {
        await WriteGate.WaitAsync(stop).ConfigureAwait(false);
        string temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, usage, cancellationToken: stop).ConfigureAwait(false);
                await stream.FlushAsync(stop).ConfigureAwait(false);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            WriteGate.Release();
        }
    }
}
