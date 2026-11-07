using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using WinLight.App.Native;
using WinLight.App.ViewModels;
using WinLight.Core;
using Windows.System;
using Windows.Win32;

namespace WinLight.App;

public sealed partial class App : Application
{
    private readonly CancellationTokenSource _stop = new();
    private readonly AppSearch _search = new();
    private readonly LauncherViewModel _viewModel = new();
    private readonly ObservableCollection<AppResultViewModel> _visibleResults = [];
    private readonly Dictionary<string, AppResultViewModel> _resultCache = new(StringComparer.OrdinalIgnoreCase);
    private Window? _window;
    private TextBox? _input;
    private ListView? _results;
    private TextBlock? _error;
    private TaskbarIcon? _tray;
    private HotkeyWindow? _hotkey;
    private AppIndexer? _indexer;
    private ShellIconLoader? _icons;
    private DispatcherQueue? _dispatcher;
    private bool _showing;
    private bool _windowActive;
    private bool _hasIndex;
    private DateTimeOffset _lastIndexed;
    private int _logicalHeight = 56;
    private long _pendingHotkeyFrame;
    private int _hideCount;

    partial void ConfigureVisuals(StackPanel root, TextBox input, ListView results,
        TextBlock error, LauncherViewModel viewModel);

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => AppLog.Error("Unhandled UI exception", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _icons = new ShellIconLoader(_dispatcher);
        _input = new TextBox { PlaceholderText = "Search apps", Height = 56 };
        _input.KeyDown += OnInputKeyDown;
        _input.TextChanged += (_, _) =>
        {
            if (PerfLog.Enabled) LogInputState("text_changed");
            long stamp = PerfLog.Enabled ? Stopwatch.GetTimestamp() : 0;
            UpdateResults(stamp);
        };
        _results = new ListView
        {
            DisplayMemberPath = nameof(AppResultViewModel.Name),
            IsItemClickEnabled = true,
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 384,
            Visibility = Visibility.Collapsed,
            ItemsSource = _visibleResults
        };
        _results.ItemClick += (_, e) => { if (e.ClickedItem is AppResultViewModel item) Launch(item.Item); };
        _results.SelectionChanged += (_, _) =>
        {
            if (DiagnosticLog.Enabled)
                DiagnosticLog.Write($"Selected result: {(_results.SelectedItem as AppResultViewModel)?.Name ?? "none"}");
        };
        _error = new TextBlock { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel();
        panel.Children.Add(_input);
        panel.Children.Add(_results);
        panel.Children.Add(_error);
        ConfigureVisuals(panel, _input, _results, _error, _viewModel);
        _window = new Window { Content = panel };
        OverlayWindow.Configure(_window);
        OverlayWindow.Place(_window, 56);
        _window.AppWindow.Closing += (_, e) => { e.Cancel = true; Hide(); };
        _window.Activated += (_, e) =>
        {
            _windowActive = e.WindowActivationState != WindowActivationState.Deactivated;
            if (e.WindowActivationState == WindowActivationState.Deactivated) Hide();
            else if (_showing) _dispatcher?.TryEnqueue(() => _input?.Focus(FocusState.Programmatic));
        };
        _window.AppWindow.Hide();

        _tray = new TaskbarIcon
        {
            ToolTipText = "WinLight — Alt+Space",
            Icon = System.Drawing.SystemIcons.Application,
            LeftClickCommand = new RelayCommand(Toggle),
            NoLeftClickDelay = true
        };
        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        exitItem.Click += (_, _) => ExitApp();
        var menu = new MenuFlyout();
        menu.Items.Add(exitItem);
        _tray.ContextFlyout = menu;
        try { _tray.ForceCreate(false); }
        catch (InvalidOperationException error)
        {
            AppLog.Error("Create tray icon", error);
            _tray.Dispose();
            _tray = null;
            _error.Text = "Tray icon unavailable; Alt+Space still works";
            _error.Visibility = Visibility.Visible;
            Show();
        }

        _hotkey = new HotkeyWindow(OnHotkey);
        if (!_hotkey.RegisterAltSpace())
        {
            if (_tray is not null) _tray.ToolTipText = "WinLight — Alt+Space unavailable; click to open";
            if (_tray?.IsCreated == true)
                _tray.ShowNotification("WinLight", "Alt+Space is in use. Click the tray icon to open WinLight.");
            else
            {
                _error.Text = "Alt+Space is already in use";
                _error.Visibility = Visibility.Visible;
                Show();
            }
        }
        _indexer = new AppIndexer(OnIndexUpdated);
        _ = LoadIndexThenRefreshAsync();
        _ = LoadUsageAsync();
        RegisterStartup();
        _ = ListenForSecondInstanceAsync(_stop.Token);
    }

    private void OnIndexUpdated(IReadOnlyList<AppItem> items)
    {
        if (items.Count > 0) _ = SaveIndexAsync(items);
        _dispatcher?.TryEnqueue(() => PublishIndex(items, isComplete: true));
    }

    private async Task LoadIndexThenRefreshAsync()
    {
        AppItem[]? cached = null;
        long loadStart = PerfLog.Stamp();
        try
        {
            cached = await Task.Run(() => IndexSnapshotStore.LoadAsync(IndexSnapshotStore.DefaultPath,
                _stop.Token), _stop.Token).ConfigureAwait(false);
            if (PerfLog.Enabled) PerfLog.Record("index_snapshot_load", loadStart,
                $"items={cached?.Length ?? 0}");
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
        catch (Exception error) { AppLog.Error("Load index snapshot", error); }
        _dispatcher?.TryEnqueue(() =>
        {
            if (cached is { Length: > 0 }) PublishIndex(cached, isComplete: false);
            _indexer?.Refresh();
        });
    }

    private void PublishIndex(IReadOnlyList<AppItem> items, bool isComplete)
    {
        long dedupStart = PerfLog.Stamp();
        _search.SetItems(items);
        if (PerfLog.Enabled) PerfLog.Record("index_deduplicate", dedupStart, $"raw={items.Count};unique={_search.Count}");
        if (!_hasIndex && _search.Count > 0 && PerfLog.Enabled)
            PerfLog.RecordStartup("index_usable", $"items={_search.Count};source={(isComplete ? "scan" : "snapshot")}");
        _resultCache.Clear();
        if (DiagnosticLog.Enabled)
            DiagnosticLog.Write($"Index count after {(isComplete ? "refresh" : "snapshot")}: {_search.Count}");
        _hasIndex = _search.Count > 0;
        if (isComplete)
        {
            if (PerfLog.Enabled) PerfLog.RecordStartup("index_complete", $"items={_search.Count}");
            _lastIndexed = DateTimeOffset.UtcNow;
        }
        UpdateResults();
    }

    private static async Task SaveIndexAsync(IReadOnlyList<AppItem> items)
    {
        try { await IndexSnapshotStore.SaveAsync(IndexSnapshotStore.DefaultPath, items).ConfigureAwait(false); }
        catch (Exception error) { AppLog.Error("Save index snapshot", error); }
    }

    private void UpdateResults(long perfStart = 0)
    {
        if (_input is null || _results is null || _window is null) return;
        _viewModel.SetQuery(_input.Text);
        IReadOnlyList<AppItem> matches = _viewModel.IsResultsVisible
            ? _search.Find(_input.Text) : Array.Empty<AppItem>();
        if (DiagnosticLog.Enabled) DiagnosticLog.Write($"Query: '{_input.Text}', results: {matches.Count}");
        _visibleResults.Clear();
        foreach (AppItem item in matches)
        {
            long iconLookup = PerfLog.Stamp();
            if (!_resultCache.TryGetValue(item.LaunchPath, out AppResultViewModel? result))
            {
                result = new AppResultViewModel(item);
                _resultCache.Add(item.LaunchPath, result);
                _icons?.Queue(result);
            }
            else if (PerfLog.Enabled && result.Icon is not null)
                PerfLog.Record("icon_cached", iconLookup, $"name={result.Name}");
            _visibleResults.Add(result);
        }
        _results.Visibility = _viewModel.IsResultsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (DiagnosticLog.Enabled) DiagnosticLog.Write($"ListView ItemsSource count: {matches.Count}");
        _results.SelectedIndex = matches.Count == 0 ? -1 : 0;
        _results.Height = matches.Count * 48;
        int logicalHeight = 56 + matches.Count * 48 + (_error?.Visibility == Visibility.Visible ? 32 : 0);
        if (logicalHeight != _logicalHeight)
        {
            _logicalHeight = logicalHeight;
            OverlayWindow.ResizeHeight(_window, logicalHeight);
        }
        if (PerfLog.Enabled && perfStart != 0)
            PerfLog.Record("query_update", perfStart, $"query={_input.Text.Replace('|', ' ')};results={matches.Count};index={_search.Count}");
    }

    private void OnHotkey(long stamp)
    {
        _pendingHotkeyFrame = stamp;
        Toggle();
        if (stamp == 0 || !_showing) _pendingHotkeyFrame = 0;
        else CompositionTarget.Rendering += OnFirstRenderedFrame;
    }

    private void OnFirstRenderedFrame(object? sender, object args)
    {
        if (_pendingHotkeyFrame == 0 || _window?.AppWindow.IsVisible != true ||
            _input?.FocusState == FocusState.Unfocused) return;
        CompositionTarget.Rendering -= OnFirstRenderedFrame;
        long stamp = _pendingHotkeyFrame;
        _pendingHotkeyFrame = 0;
        PerfLog.Record("hotkey_first_frame", stamp);
    }

    private void Toggle()
    {
        if (_window?.AppWindow.IsVisible == true) Hide();
        else Show();
    }

    private void Show()
    {
        if (_window is null) return;
        if (_hasIndex && DateTimeOffset.UtcNow - _lastIndexed >= TimeSpan.FromMinutes(15))
        {
            _lastIndexed = DateTimeOffset.UtcNow;
            _indexer?.Refresh();
        }
        _showing = true;
        if (_input is not null) _input.Text = string.Empty;
        UpdateResults();
        OverlayWindow.Place(_window, _logicalHeight);
        _window.AppWindow.Show();
        OverlayWindow.BringToForeground(_window);
        _dispatcher?.TryEnqueue(() =>
        {
            bool focused = _input?.Focus(FocusState.Programmatic) == true;
            if (DiagnosticLog.Enabled) DiagnosticLog.Write($"TextBox focus after activation: {focused}");
        });
    }

    private void Hide()
    {
        _showing = false;
        if (PerfLog.Enabled && ++_hideCount % 500 == 0)
        {
            using Process process = Process.GetCurrentProcess();
            var gc = GC.GetGCMemoryInfo();
            PerfLog.Point("memory_cycle",
                $"cycles={_hideCount};cache={_resultCache.Count};icon_queue={_icons?.QueuedCount ?? 0};managed_bytes={GC.GetTotalMemory(false)};gc_heap_bytes={gc.HeapSizeBytes};working_bytes={process.WorkingSet64};private_bytes={process.PrivateMemorySize64};handles={process.HandleCount};threads={process.Threads.Count}");
        }
        if (_pendingHotkeyFrame != 0)
        {
            _pendingHotkeyFrame = 0;
            CompositionTarget.Rendering -= OnFirstRenderedFrame;
        }
        _window?.AppWindow.Hide();
    }

    private void Launch(AppItem item)
    {
        try
        {
            bool admin = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            AppLauncher.Launch(item, admin);
            if (DiagnosticLog.Enabled) DiagnosticLog.Write($"Launched result: {item.Name}, admin: {admin}");
            _search.RecordLaunch(item, DateTimeOffset.UtcNow);
            _ = SaveUsageAsync(_search.SnapshotUsage());
            if (_error is not null) _error.Visibility = Visibility.Collapsed;
            Hide();
        }
        catch (Exception error)
        {
            AppLog.Error($"Launch {item.Name}", error);
            if (_error is not null)
            {
                _error.Text = $"Could not open {item.Name}: {error.Message}";
                _error.Visibility = Visibility.Visible;
            }
            UpdateResults();
        }
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (PerfLog.Enabled) LogInputState($"key_down_{e.Key}");
        if (e.Key == VirtualKey.Escape) { Hide(); e.Handled = true; }
        else if (e.Key == VirtualKey.Down && _results is not null && _results.Items.Count > 0)
        {
            _results.SelectedIndex = Math.Min(_results.SelectedIndex + 1, _results.Items.Count - 1);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Up && _results is not null && _results.Items.Count > 0)
        {
            _results.SelectedIndex = Math.Max(_results.SelectedIndex - 1, 0);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter && _results?.SelectedItem is AppResultViewModel item)
        {
            Launch(item.Item);
            e.Handled = true;
        }
    }

    private unsafe void LogInputState(string eventName)
    {
        if (!PerfLog.Enabled) return;
        long stamp = PerfLog.Stamp();
        nint foreground = (nint)PInvoke.GetForegroundWindow().Value;
        nint own = _window is null ? 0 : WinRT.Interop.WindowNative.GetWindowHandle(_window);
        PerfLog.Record(eventName, stamp,
            $"active={_windowActive};visible={_window?.AppWindow.IsVisible};focus={_input?.FocusState};foreground={foreground};own={own};text={_input?.Text.Replace('|', ' ')}");
    }

    private void ExitApp()
    {
        _stop.Cancel();
        _indexer?.Dispose();
        _icons?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        Application.Current.Exit();
    }

    private async Task LoadUsageAsync()
    {
        try
        {
            Dictionary<string, UsageRecord> usage = await UsageStore.LoadAsync(UsageStore.DefaultPath, _stop.Token)
                .ConfigureAwait(false);
            _dispatcher?.TryEnqueue(() => { _search.LoadUsage(usage); UpdateResults(); });
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { AppLog.Error("Load usage", error); }
    }

    private static async Task SaveUsageAsync(IReadOnlyDictionary<string, UsageRecord> usage)
    {
        try { await UsageStore.SaveAsync(UsageStore.DefaultPath, usage).ConfigureAwait(false); }
        catch (Exception error) { AppLog.Error("Save usage", error); }
    }

    private async Task ListenForSecondInstanceAsync(CancellationToken stop)
    {
        byte[] signal = new byte[1];
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(Program.PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
                if (await pipe.ReadAsync(signal, stop).ConfigureAwait(false) == 1 && signal[0] == 1)
                    _dispatcher?.TryEnqueue(Show);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (IOException error) { AppLog.Error("Second-instance pipe", error); }
            catch (Exception error)
            {
                AppLog.Error("Second-instance listener", error);
                try { await Task.Delay(1000, stop).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            }
        }
    }

    private static void RegisterStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key.SetValue("WinLight", $"\"{Environment.ProcessPath}\"");
        }
        catch (Exception error) when (error is SecurityException or UnauthorizedAccessException)
        { AppLog.Error("Register login startup", error); }
    }
}
