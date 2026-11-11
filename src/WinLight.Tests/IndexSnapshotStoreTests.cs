using WinLight.Core;

namespace WinLight.Tests;

public sealed class IndexSnapshotStoreTests
{
    [Fact]
    public async Task RoundTripPreservesLaunchMetadataAndMarksEntriesAsCached()
    {
        string path = NewPath();
        try
        {
            var item = new AppItem("Calculator", "shell:AppsFolder\\Calculator", "Calculator",
                AppSource.AppsFolder, @"C:\Apps\Calculator.exe", "Calculator.AUMID", "--safe");
            await IndexSnapshotStore.SaveAsync(path, [item]);

            AppItem[]? loaded = await IndexSnapshotStore.LoadAsync(path);

            Assert.NotNull(loaded);
            AppItem restored = Assert.Single(loaded);
            Assert.Equal(item.Name, restored.Name);
            Assert.Equal(item.LaunchPath, restored.LaunchPath);
            Assert.Equal(item.TargetPath, restored.TargetPath);
            Assert.Equal(item.AppUserModelId, restored.AppUserModelId);
            Assert.Equal(item.Arguments, restored.Arguments);
            Assert.Equal(item.Source, restored.Source);
            Assert.True(restored.IsFromSnapshot);
        }
        finally { DeleteFolder(path); }
    }

    [Fact]
    public async Task CorruptSnapshotFallsBackToScan()
    {
        string path = NewPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "{ not valid json");

            Assert.Null(await IndexSnapshotStore.LoadAsync(path));
        }
        finally { DeleteFolder(path); }
    }

    [Fact]
    public async Task OldVersionFallsBackToScan()
    {
        string path = NewPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "{\"Version\":0,\"Items\":[]}");

            Assert.Null(await IndexSnapshotStore.LoadAsync(path));
        }
        finally { DeleteFolder(path); }
    }

    [Fact]
    public async Task MissingSnapshotFallsBackToScan()
    {
        Assert.Null(await IndexSnapshotStore.LoadAsync(NewPath()));
    }

    [Fact]
    public async Task CachedAppsFolderIdentityDoesNotDependOnResolvedTarget()
    {
        string path = NewPath();
        string missingTarget = Path.Combine(Path.GetDirectoryName(path)!, "missing.exe");
        try
        {
            await IndexSnapshotStore.SaveAsync(path,
                [new AppItem("Removed app", "shell:AppsFolder\\Removed", "Removed",
                    AppSource.AppsFolder, missingTarget)]);

            AppItem item = Assert.Single((await IndexSnapshotStore.LoadAsync(path))!);
            Assert.False(item.HasMissingSnapshotTarget());
            Assert.False(new AppItem(item.Name, item.LaunchPath, item.Identity,
                item.Source, item.TargetPath).HasMissingSnapshotTarget());
        }
        finally { DeleteFolder(path); }
    }

    [Fact]
    public void CachedMissingShortcutIsDetectedBeforeLaunch()
    {
        string missingShortcut = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".lnk");
        var item = new AppItem("Removed shortcut", missingShortcut, "Removed",
            AppSource.UserStartMenu, fromSnapshot: true);

        Assert.True(item.HasMissingSnapshotTarget());
    }

    private static string NewPath() => Path.Combine(Path.GetTempPath(),
        "WinLight.Tests", Guid.NewGuid().ToString("N"), "index.json");

    private static void DeleteFolder(string path)
    {
        string? folder = Path.GetDirectoryName(path);
        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }
}
