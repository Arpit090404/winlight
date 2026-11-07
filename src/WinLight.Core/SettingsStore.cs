using System.Text.Json;

namespace WinLight.Core;

public static class SettingsStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinLight", "settings.json");

    public static async Task<WinLightSettings> LoadAsync(string path, CancellationToken stop = default) =>
        (await ReadAsync(path, stop).ConfigureAwait(false)).Settings;

    public static async Task<Dictionary<string, UsageRecord>> LoadUsageAsync(string path, CancellationToken stop = default) =>
        new((await ReadAsync(path, stop).ConfigureAwait(false)).Usage, StringComparer.OrdinalIgnoreCase);

    public static async Task SaveAsync(string path, WinLightSettings settings, CancellationToken stop = default)
    {
        if (!settings.Hotkey.IsValid(out string reason)) throw new ArgumentException(reason, nameof(settings));
        await Gate.WaitAsync(stop).ConfigureAwait(false);
        try
        {
            SettingsDocument existing = await ReadAsync(path, stop).ConfigureAwait(false);
            await WriteAsync(path, new SettingsDocument(settings, existing.Usage), stop).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    public static async Task SaveUsageAsync(string path, IReadOnlyDictionary<string, UsageRecord> usage,
        CancellationToken stop = default)
    {
        await Gate.WaitAsync(stop).ConfigureAwait(false);
        try
        {
            SettingsDocument existing = await ReadAsync(path, stop).ConfigureAwait(false);
            await WriteAsync(path, new SettingsDocument(existing.Settings,
                new Dictionary<string, UsageRecord>(usage, StringComparer.OrdinalIgnoreCase)), stop).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    private static async Task<SettingsDocument> ReadAsync(string path, CancellationToken stop)
    {
        if (!File.Exists(path)) return new SettingsDocument();
        try
        {
            await using FileStream stream = File.OpenRead(path);
            using JsonDocument json = await JsonDocument.ParseAsync(stream, cancellationToken: stop).ConfigureAwait(false);
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new SettingsDocument();
            if (!root.TryGetProperty("Version", out _))
            {
                Dictionary<string, UsageRecord>? legacy = root.Deserialize<Dictionary<string, UsageRecord>>(JsonOptions);
                return new SettingsDocument(new WinLightSettings(), legacy ?? []);
            }
            StoredDocument? stored = root.Deserialize<StoredDocument>(JsonOptions);
            if (stored?.Version != WinLightSettings.CurrentVersion ||
                !stored.Hotkey.IsValid(out _)) return new SettingsDocument();
            return new SettingsDocument(new WinLightSettings
            {
                Hotkey = stored.Hotkey,
                StartAtLogin = stored.StartAtLogin
            }, stored.Usage ?? []);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        { return new SettingsDocument(); }
    }

    private static async Task WriteAsync(string path, SettingsDocument document, CancellationToken stop)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, new StoredDocument(
                    document.Settings.Version, document.Settings.Hotkey,
                    document.Settings.StartAtLogin, document.Usage), JsonOptions, stop).ConfigureAwait(false);
                await stream.FlushAsync(stop).ConfigureAwait(false);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record SettingsDocument(WinLightSettings Settings, Dictionary<string, UsageRecord> Usage)
    {
        public SettingsDocument() : this(new WinLightSettings(), new(StringComparer.OrdinalIgnoreCase)) { }
    }

    private sealed record StoredDocument(int Version, HotkeyBinding Hotkey, bool StartAtLogin,
        Dictionary<string, UsageRecord>? Usage);
}
