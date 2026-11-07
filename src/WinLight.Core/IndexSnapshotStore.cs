using System.Text.Json;

namespace WinLight.Core;

public static class IndexSnapshotStore
{
    public const int CurrentVersion = 1;
    private static readonly SemaphoreSlim SaveGate = new(1, 1);

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinLight", "index.json");

    public static async Task<AppItem[]?> LoadAsync(string path, CancellationToken stop = default)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.Asynchronous);
            Snapshot? snapshot = await JsonSerializer.DeserializeAsync<Snapshot>(stream,
                cancellationToken: stop).ConfigureAwait(false);
            if (snapshot?.Version != CurrentVersion || snapshot.Items is null) return null;

            var items = new AppItem[snapshot.Items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                Entry entry = snapshot.Items[i];
                if (string.IsNullOrWhiteSpace(entry.Name) ||
                    string.IsNullOrWhiteSpace(entry.LaunchPath) ||
                    string.IsNullOrWhiteSpace(entry.Identity)) return null;
                items[i] = new AppItem(entry.Name, entry.LaunchPath, entry.Identity,
                    entry.Source, entry.TargetPath, entry.AppUserModelId, entry.Arguments,
                    fromSnapshot: true);
            }
            return items;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static async Task SaveAsync(string path, IReadOnlyList<AppItem> items,
        CancellationToken stop = default)
    {
        await SaveGate.WaitAsync(stop).ConfigureAwait(false);
        string? temp = null;
        try
        {
            string directory = Path.GetDirectoryName(path)
                ?? throw new ArgumentException("Snapshot path needs a directory.", nameof(path));
            Directory.CreateDirectory(directory);
            temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            var entries = new Entry[items.Count];
            for (int i = 0; i < entries.Length; i++)
            {
                AppItem item = items[i];
                entries[i] = new Entry
                {
                    Name = item.Name,
                    LaunchPath = item.LaunchPath,
                    Identity = item.Identity,
                    Source = item.Source,
                    TargetPath = item.TargetPath,
                    AppUserModelId = item.AppUserModelId,
                    Arguments = item.Arguments
                };
            }
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream,
                    new Snapshot { Version = CurrentVersion, Items = entries },
                    cancellationToken: stop).ConfigureAwait(false);
                await stream.FlushAsync(stop).ConfigureAwait(false);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (temp is not null && File.Exists(temp)) File.Delete(temp);
            SaveGate.Release();
        }
    }

    private sealed class Snapshot
    {
        public int Version { get; set; }
        public Entry[]? Items { get; set; }
    }

    private sealed class Entry
    {
        public string Name { get; set; } = string.Empty;
        public string LaunchPath { get; set; } = string.Empty;
        public string Identity { get; set; } = string.Empty;
        public AppSource Source { get; set; }
        public string? TargetPath { get; set; }
        public string? AppUserModelId { get; set; }
        public string? Arguments { get; set; }
    }
}
