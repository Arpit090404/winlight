namespace WinLight.Core;

public static class UsageStore
{
    public static string DefaultPath => SettingsStore.DefaultPath;

    public static Task<Dictionary<string, UsageRecord>> LoadAsync(string path, CancellationToken stop = default) =>
        SettingsStore.LoadUsageAsync(path, stop);

    public static Task SaveAsync(string path, IReadOnlyDictionary<string, UsageRecord> usage,
        CancellationToken stop = default) => SettingsStore.SaveUsageAsync(path, usage, stop);
}
