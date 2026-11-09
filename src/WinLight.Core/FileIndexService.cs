using System.Collections.Concurrent;
using System.Security;

namespace WinLight.Core;

// All mutations run on one worker; queries see only completed, immutable snapshots.
public sealed class FileIndexService : IDisposable
{
    private readonly string[] _roots;
    private readonly FileSearchProvider _provider;
    private readonly Action<string, Exception> _log;
    private readonly Action? _updated;
    private readonly ConcurrentQueue<(string Path, string? OldPath, bool Reconcile)> _events = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _eventGate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private Dictionary<string, FileEntry> _trees = new(StringComparer.OrdinalIgnoreCase);
    private readonly Task _worker;
    private int _resetWatchers;
    private bool _disposed;

    public FileIndexService(IEnumerable<string> roots, FileSearchProvider provider,
        Action<string, Exception> log, Action? updated = null)
    {
        _roots = roots.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _provider = provider;
        _log = log;
        _updated = updated;
        _worker = Task.Factory.StartNew(Work, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        RequestReconcile();
    }

    public Task InitialScan => _workerInitial.Task;
    private readonly TaskCompletionSource _workerInitial = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void RequestReconcile() => Enqueue(string.Empty, null, true);

    private void Enqueue(string path, string? oldPath, bool reconcile)
    {
        lock (_eventGate)
        {
            if (_disposed) return;
            _events.Enqueue((path, oldPath, reconcile));
            _signal.Release();
        }
    }

    private void Work()
    {
        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        try
        {
            while (true)
            {
                _signal.Wait(_stop.Token);
                try
                {
                    Thread.Sleep(150);
                    _stop.Token.ThrowIfCancellationRequested();
                    bool reconcile = false;
                    var changes = new List<(string Path, string? OldPath)>();
                    while (_events.TryDequeue(out var change))
                    {
                        reconcile |= change.Reconcile;
                        if (!change.Reconcile) changes.Add((change.Path, change.OldPath));
                    }
                    if (reconcile)
                    {
                        ScanAll(_stop.Token);
                        if (Interlocked.Exchange(ref _resetWatchers, 0) != 0)
                        {
                            foreach (FileSystemWatcher watcher in _watchers) watcher.Dispose();
                            _watchers.Clear();
                        }
                        EnsureWatchers();
                        _workerInitial.TrySetResult();
                    }
                    else
                    {
                        foreach (var change in changes)
                        {
                            _stop.Token.ThrowIfCancellationRequested();
                            if (change.OldPath is not null) RemovePath(change.OldPath);
                            RemovePath(change.Path);
                            ScanPath(change.Path, _stop.Token);
                        }
                    }
                    PublishEntries();
                    _updated?.Invoke();
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    _log("File index worker cycle", error);
                    Thread.Sleep(1000);
                    RequestReconcile();
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            _workerInitial.TrySetCanceled(_stop.Token);
        }
        catch (Exception error)
        {
            _workerInitial.TrySetException(error);
            _log("File index worker", error);
        }
        finally
        {
            foreach (FileSystemWatcher watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
        }
    }

    private void ScanAll(CancellationToken stop)
    {
        var next = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in _roots)
        {
            stop.ThrowIfCancellationRequested();
            try
            {
                FileAttributes attributes = File.GetAttributes(root);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) !=
                    FileAttributes.Directory) continue;
                FileEntry rootEntry = FileEntry.Root(root);
                next.Add(root, rootEntry);
                ScanChildren(root, rootEntry, stop);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
            { _log($"Scan root {root}", error); }
        }
        _trees = next;
    }

    private void ScanPath(string path, CancellationToken stop)
    {
        if (!TryParent(path, out FileEntry? parent, out string name))
        {
            if (IsWithinRoots(path)) RequestReconcile();
            return;
        }
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) return;
            bool directory = (attributes & FileAttributes.Directory) != 0;
            FileEntry child = FileEntry.Child(name, parent!, directory);
            parent!.Children![name] = child;
            if (directory) ScanChildren(path, child, stop);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        { _log($"Scan {path}", error); }
    }

    private void ScanChildren(string root, FileEntry rootEntry, CancellationToken stop)
    {
        var pending = new Stack<(string Path, FileEntry Parent)>();
        pending.Push((root, rootEntry));
        while (pending.Count > 0)
        {
            stop.ThrowIfCancellationRequested();
            (string current, FileEntry parent) = pending.Pop();
            try
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(current))
                {
                    stop.ThrowIfCancellationRequested();
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        bool directory = (attributes & FileAttributes.Directory) != 0;
                        string name = System.IO.Path.GetFileName(path);
                        FileEntry child = FileEntry.Child(name, parent, directory);
                        parent.Children![name] = child;
                        if (directory) pending.Push((path, child));
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
                    { _log($"Scan {path}", error); }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
            { _log($"Scan {current}", error); }
        }
    }

    private void PublishEntries()
    {
        var entries = new List<FileEntry>();
        var pending = new Stack<FileEntry>(_trees.Values);
        while (pending.Count > 0)
        {
            FileEntry parent = pending.Pop();
            foreach (FileEntry child in parent.Children!.Values)
            {
                entries.Add(child);
                if (child.IsDirectory) pending.Push(child);
            }
        }
        _provider.PublishUnique(entries);
    }

    private bool IsWithinRoots(string path)
    {
        string full = Path.GetFullPath(path);
        foreach (string root in _roots)
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void RemovePath(string path)
    {
        if (TryParent(path, out FileEntry? parent, out string name)) parent!.Children!.Remove(name);
    }

    private bool TryParent(string path, out FileEntry? parent, out string name)
    {
        parent = null;
        name = string.Empty;
        if (!IsWithinRoots(path)) return false;
        foreach (string root in _roots)
        {
            if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)) continue;
            if (!_trees.TryGetValue(root, out parent)) return false;
            string relative = Path.GetRelativePath(root, path);
            string[] parts = relative.Split(Path.DirectorySeparatorChar);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parent.Children is null || !parent.Children.TryGetValue(parts[i], out FileEntry? child))
                    return false;
                parent = child;
            }
            name = parts[^1];
            return name.Length > 0 && parent.Children is not null;
        }
        return false;
    }

    private void EnsureWatchers()
    {
        foreach (string root in _roots)
        {
            if (_watchers.Any(w => w.Path.Equals(root, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                if (!Directory.Exists(root))
                {
                    WatchMissingRoot(root);
                    continue;
                }
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    InternalBufferSize = 32768
                };
                watcher.Created += (_, e) => Enqueue(e.FullPath, null, false);
                watcher.Deleted += (_, e) => Enqueue(e.FullPath, null, false);
                watcher.Renamed += (_, e) => Enqueue(e.FullPath, e.OldFullPath, false);
                watcher.Error += (_, e) =>
                {
                    _log($"Watch {root}", e.GetException());
                    Interlocked.Exchange(ref _resetWatchers, 1);
                    Enqueue(string.Empty, null, true);
                };
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
            { _log($"Watch {root}", error); }
        }
    }

    private void WatchMissingRoot(string root)
    {
        string? parent = Path.GetDirectoryName(root);
        if (parent is null || !Directory.Exists(parent)) return;
        string name = Path.GetFileName(root);
        if (_watchers.Any(w => w.Path.Equals(parent, StringComparison.OrdinalIgnoreCase) &&
            w.Filter.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
        var watcher = new FileSystemWatcher(parent, name)
        {
            NotifyFilter = NotifyFilters.DirectoryName
        };
        watcher.Created += (_, _) => RequestReconcile();
        watcher.Deleted += (_, _) => RequestReconcile();
        watcher.Renamed += (_, _) => RequestReconcile();
        watcher.Error += (_, e) =>
        {
            _log($"Watch missing root {root}", e.GetException());
            Interlocked.Exchange(ref _resetWatchers, 1);
            RequestReconcile();
        };
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    public void Dispose()
    {
        lock (_eventGate) _disposed = true;
        _stop.Cancel();
        _ = _worker.ContinueWith(_ =>
        {
            lock (_eventGate) _signal.Dispose();
            _stop.Dispose();
        }, TaskScheduler.Default);
    }
}
