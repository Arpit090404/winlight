using System.Runtime.InteropServices;
using WinLight.Core;

namespace WinLight.App;

internal sealed class AppIndexer : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Action<IReadOnlyList<AppItem>> _updated;
    private readonly object _debounceGate = new();
    private CancellationTokenSource? _debounce;
    private int _refreshing;
    private int _pending;
    private bool _disposed;

    public AppIndexer(Action<IReadOnlyList<AppItem>> updated)
    {
        _updated = updated;
        foreach ((string folder, _) in StartMenuFolders())
        {
            if (!Directory.Exists(folder)) continue;
            var watcher = new FileSystemWatcher(folder, "*.lnk")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            watcher.Changed += Changed;
            watcher.Created += Changed;
            watcher.Deleted += Changed;
            watcher.Renamed += Renamed;
            _watchers.Add(watcher);
        }
    }

    public void Refresh()
    {
        if (_disposed) return;
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
        {
            Interlocked.Exchange(ref _pending, 1);
            return;
        }
        var thread = new Thread(() =>
        {
            long stamp = PerfLog.Stamp();
            try
            {
                IReadOnlyList<AppItem> items = Enumerate();
                if (PerfLog.Enabled) PerfLog.Record("index_refresh", stamp, $"raw_items={items.Count}");
                _updated(items);
            }
            catch (Exception error) { AppLog.Error("Enumerate applications", error); }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
                if (Interlocked.Exchange(ref _pending, 0) != 0) Refresh();
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static IReadOnlyList<AppItem> Enumerate()
    {
        var results = new List<AppItem>();
        object? shellObject = null;
        object? folderObject = null;
        try
        {
            Type shellType = Type.GetTypeFromProgID("Shell.Application")
                ?? throw new InvalidOperationException("Shell.Application unavailable");
            shellObject = Activator.CreateInstance(shellType);
            dynamic shell = shellObject!;
            folderObject = shell.NameSpace("shell:AppsFolder");
            if (folderObject is not null)
            {
                dynamic folder = folderObject;
                dynamic items = folder.Items();
                int count = items.Count;
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic item = items.Item(i);
                        string name = item.Name;
                        string path = item.Path;
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                        string? target = Convert.ToString(item.ExtendedProperty("System.Link.TargetParsingPath"));
                        string? aumid = Convert.ToString(item.ExtendedProperty("System.AppUserModel.ID"));
                        string? arguments = Convert.ToString(item.ExtendedProperty("System.Link.Arguments"));
                        string launch = path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                            ? path : $"shell:AppsFolder\\{path}";
                        results.Add(new AppItem(name, launch, NormalizeIdentity(path),
                            AppSource.AppsFolder, NormalizeIdentity(target ?? string.Empty), aumid, arguments));
                    }
                    catch (Exception error) { AppLog.Error($"AppsFolder item {i}", error); }
                }
            }
        }
        catch (Exception error) { AppLog.Error("AppsFolder enumeration", error); }
        finally
        {
            if (folderObject is not null && Marshal.IsComObject(folderObject)) Marshal.FinalReleaseComObject(folderObject);
            if (shellObject is not null && Marshal.IsComObject(shellObject)) Marshal.FinalReleaseComObject(shellObject);
        }

        object? shortcutShellObject = null;
        object? shortcutMetadataShellObject = null;
        try
        {
            Type? shortcutType = Type.GetTypeFromProgID("WScript.Shell");
            if (shortcutType is not null) shortcutShellObject = Activator.CreateInstance(shortcutType);
            Type? metadataShellType = Type.GetTypeFromProgID("Shell.Application");
            if (metadataShellType is not null) shortcutMetadataShellObject = Activator.CreateInstance(metadataShellType);
            foreach ((string folder, AppSource source) in StartMenuFolders())
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string path in Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories))
                {
                    try
                    {
                        string name = Path.GetFileNameWithoutExtension(path);
                        string? target = null;
                        string? aumid = null;
                        string? arguments = null;
                        if (shortcutShellObject is not null)
                        {
                            dynamic shortcutShell = shortcutShellObject;
                            dynamic shortcut = shortcutShell.CreateShortcut(path);
                            target = shortcut.TargetPath;
                            arguments = shortcut.Arguments;
                        }
                        dynamic folderItem = shortcutMetadataShellObject is null ? null! :
                            ((dynamic)shortcutMetadataShellObject).NameSpace(Path.GetDirectoryName(path)!).ParseName(Path.GetFileName(path));
                        if (folderItem is not null)
                        {
                            string? shellTarget = Convert.ToString(folderItem.ExtendedProperty("System.Link.TargetParsingPath"));
                            if (!string.IsNullOrWhiteSpace(shellTarget)) target = shellTarget;
                            aumid = Convert.ToString(folderItem.ExtendedProperty("System.AppUserModel.ID"));
                        }
                        results.Add(new AppItem(name, path, NormalizeIdentity(target ?? path),
                            source, NormalizeIdentity(target ?? string.Empty), aumid, arguments));
                    }
                    catch (Exception error) { AppLog.Error($"Shortcut {path}", error); }
                }
            }
        }
        catch (Exception error) { AppLog.Error("Start Menu enumeration", error); }
        finally
        {
            if (shortcutShellObject is not null && Marshal.IsComObject(shortcutShellObject))
                Marshal.FinalReleaseComObject(shortcutShellObject);
            if (shortcutMetadataShellObject is not null && Marshal.IsComObject(shortcutMetadataShellObject))
                Marshal.FinalReleaseComObject(shortcutMetadataShellObject);
        }
        return results;
    }

    private static IEnumerable<(string Folder, AppSource Source)> StartMenuFolders()
    {
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppSource.UserStartMenu);
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppSource.CommonStartMenu);
    }

    private static string NormalizeIdentity(string value)
    {
        const string programFiles64 = "{6D809377-6AF0-444B-8957-A3773F02200E}";
        const string programFiles32 = "{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}";
        if (value.Length > programFiles64.Length && value.StartsWith(programFiles64, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                value[(programFiles64.Length + 1)..]);
        if (value.Length > programFiles32.Length && value.StartsWith(programFiles32, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                value[(programFiles32.Length + 1)..]);
        return value;
    }

    private void Changed(object sender, FileSystemEventArgs args) => ScheduleRefresh();
    private void Renamed(object sender, RenamedEventArgs args) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        CancellationTokenSource source;
        lock (_debounceGate)
        {
            if (_disposed) return;
            _debounce?.Cancel();
            source = new CancellationTokenSource();
            _debounce = source;
        }
        _ = DebounceAsync(source);
    }

    private async Task DebounceAsync(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(250, source.Token).ConfigureAwait(false);
            Refresh();
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_debounceGate)
            {
                if (ReferenceEquals(_debounce, source)) _debounce = null;
            }
            source.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_debounceGate)
        {
            _disposed = true;
            _debounce?.Cancel();
        }
        foreach (FileSystemWatcher watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }
}
