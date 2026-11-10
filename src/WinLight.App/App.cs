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
    private readonly FileSearchProvider _files = new();
    private readonly SearchEngine _engine;
    private readonly LauncherViewModel _viewModel = new();
    private readonly ObservableCollection<AppResultViewModel> _visibleResults = [];
    private readonly Dictionary<string, AppResultViewModel> _resultCache = new(StringComparer.OrdinalIgnoreCase);
    private Window? _window;
    private TextBox? _input;
    private ListView? _results;
    private FrameworkElement? _pomodoroPanel;
    private bool _timerLayoutChanged;
    private TextBlock? _error;
    private TaskbarIcon? _tray;
    private HotkeyWindow? _hotkey;
    private SystemEventWindow? _systemEvents;
    private AppIndexer? _indexer;
    private FileIndexService? _fileIndexer;
    private ShellIconLoader? _icons;
    private DispatcherQueue? _dispatcher;
    private volatile bool _showing;
    private volatile bool _fileIndexDirty;
    private bool _windowActive;
    private readonly WindowActivationGate _activationGate = new();
    private bool _hasIndex;
    private DateTimeOffset _lastIndexed;
    private DateTimeOffset _lastFilesReconciled = DateTimeOffset.UtcNow;
    private int _logicalHeight = 56;
    private long _pendingHotkeyFrame;
    private int _hideCount;
    private long _resultQueryId;
    private const int SectionHeaderHeight = 28;

    partial void ConfigureVisuals(StackPanel root, TextBox input, ListView results,
        TextBlock error, LauncherViewModel viewModel);

    public App()
    {
        _engine = new SearchEngine([(new AppsProvider(_search), 1d),
            (new CalculatorProvider(), 1.1d), (_files, 0.7d)], AppLog.Error);
        _ = _files.QueryAsync("warmup", CancellationToken.None);
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            AppLog.Error("Unhandled UI exception", e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("Unobserved background task", e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Error("Unhandled process exception", e.ExceptionObject as Exception ??
                new InvalidOperationException(e.ExceptionObject.ToString()));
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
        _results.ItemClick += (_, e) => { if (e.ClickedItem is AppResultViewModel item) _ = ExecuteResultAsync(item.Result); };
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
        _pomodoroPanel = (FrameworkElement)PomodoroVisuals.CreateControls(_pomodoroViewModel,
            SelectPomodoro, StartPausePomodoro, ResetPomodoro, SkipPomodoro,
            ConfigurePomodoro, ConfigurePomodoroAutoStart);
        _pomodoroPanel.Visibility = Visibility.Collapsed;
        panel.Children.Insert(2, _pomodoroPanel);
        panel.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Escape || !_showing) return;
            Hide();
            e.Handled = true;
        };
        ResourceDictionary templates = panel.Resources.MergedDictionaries[0];
        _results.ItemTemplate = null;
        _results.ItemTemplateSelector = new ResultTemplateSelector(
            (DataTemplate)templates["WinLightResultTemplate"],
            (DataTemplate)templates["WinLightCalculatorTemplate"],
            (DataTemplate)templates["WinLightFileTemplate"]);
        _window = new Window { Content = panel };
        OverlayWindow.Configure(_window);
        OverlayWindow.Place(_window, 56);
        _window.AppWindow.Closing += (_, e) => { e.Cancel = true; Hide(); };
        _window.Activated += (_, e) =>
        {
            _windowActive = e.WindowActivationState != WindowActivationState.Deactivated;
            if (PerfLog.Enabled) PerfLog.Point("overlay_activation",
                $"state={e.WindowActivationState};showing={_showing};armed={_activationGate.ShouldHideOnDeactivated}");
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                // Showing over another topmost window can emit transient activation
                // changes before the TextBox is actually ready for input.
                if (_showing && _activationGate.ShouldHideOnDeactivated) Hide();
            }
            else if (_showing)
            {
                PostUi(() => _input?.Focus(FocusState.Programmatic));
            }
        };
        _window.AppWindow.Hide();

        _tray = new TaskbarIcon
        {
            ToolTipText = "WinLight — Alt+Space",
            Icon = System.Drawing.SystemIcons.Application,
            LeftClickCommand = new RelayCommand(Toggle),
            NoLeftClickDelay = true
        };
        var settingsItem = new MenuFlyoutItem { Text = "Settings" };
        settingsItem.Command = new RelayCommand(() =>
        {
            if (PerfLog.Enabled) PerfLog.Point("tray_settings_command",
                $"ui_thread={_dispatcher?.HasThreadAccess};thread={Environment.CurrentManagedThreadId}");
            PostUi(ShowSettings);
        });
        var rescanItem = new MenuFlyoutItem { Text = "Rescan apps" };
        rescanItem.Command = new RelayCommand(() => _indexer?.Refresh());
        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        exitItem.Command = new RelayCommand(() => PostUi(ExitApp));
        var menu = new MenuFlyout();
        menu.Items.Add(settingsItem);
        menu.Items.Add(rescanItem);
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
        _systemEvents = new SystemEventWindow(OnTaskbarCreated, OnSessionResumed);
        _ = LoadSettingsAndRegisterAsync();
        _indexer = new AppIndexer(OnIndexUpdated);
        _ = LoadIndexThenRefreshAsync();
        _ = LoadUsageAsync();
        _ = ListenForSecondInstanceAsync(_stop.Token);
        if (Environment.GetEnvironmentVariable("WINLIGHT_TEST_EXCEPTIONS") == "1")
            RunExceptionProbes();
    }

    private void OnIndexUpdated(IReadOnlyList<AppItem> items)
    {
        if (items.Count > 0) _ = SaveIndexAsync(items);
        PostUi(() =>
        {
            PublishIndex(items, isComplete: true);
            StartFileIndex();
        });
    }

    private void StartFileIndex()
    {
        if (_fileIndexer is not null) return;
        _fileIndexer = new FileIndexService([
            DownloadsPath(),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)],
            _files, AppLog.Error, () =>
            {
                _fileIndexDirty = true;
                if (_showing) PostUi(() =>
                {
                    if (!_showing) return;
                    _lastFilesReconciled = DateTimeOffset.UtcNow;
                    _resultCache.Clear();
                    _fileIndexDirty = false;
                    UpdateResults();
                });
            });
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
        PostUi(() =>
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

    private static string DownloadsPath()
    {
        const string key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
        const string folderId = "{374DE290-123F-4565-9164-39C4925E467B}";
        try
        {
            using RegistryKey? folders = Registry.CurrentUser.OpenSubKey(key);
            if (folders?.GetValue(folderId, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                is string value && !string.IsNullOrWhiteSpace(value))
                return Environment.ExpandEnvironmentVariables(value);
        }
        catch (Exception error) { AppLog.Error("Resolve Downloads folder", error); }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    private void UpdateResults(long perfStart = 0)
    {
        if (_input is null || _results is null || _window is null) return;
        bool focusVisible = PomodoroQuery.IsMatch(_input.Text);
        if (_pomodoroPanel is not null)
        {
            _timerLayoutChanged |= (_pomodoroPanel.Visibility == Visibility.Visible) != focusVisible;
            _pomodoroPanel.Visibility = focusVisible ? Visibility.Visible : Visibility.Collapsed;
        }
        _pomodoroViewModel.IsKeyboardSelected = focusVisible;
        SyncPomodoroDisplayTimer();
        _viewModel.SetQuery(_input.Text);
        long queryId = ++_resultQueryId;
        if (!_viewModel.IsResultsVisible)
        {
            _engine.CancelCurrentQuery();
            ApplyResults(Array.Empty<SearchResult>(), perfStart);
            return;
        }
        ValueTask<IReadOnlyList<SearchResult>> pending = _engine.QueryAsync(_input.Text);
        if (pending.IsCompletedSuccessfully)
            ApplyResults(pending.Result, perfStart);
        else
            _ = ApplyResultsAsync(pending, queryId, perfStart);
    }

    private async Task ApplyResultsAsync(ValueTask<IReadOnlyList<SearchResult>> pending,
        long queryId, long perfStart)
    {
        try
        {
            IReadOnlyList<SearchResult> matches = await pending.ConfigureAwait(true);
            if (queryId == _resultQueryId) ApplyResults(matches, perfStart);
        }
        catch (Exception error) { AppLog.Error("Apply search results", error); }
    }

    private void ApplyResults(IReadOnlyList<SearchResult> matches, long perfStart)
    {
        if (_input is null || _results is null || _window is null) return;
        if (DiagnosticLog.Enabled) DiagnosticLog.Write($"Query: '{_input.Text}', results: {matches.Count}");
        _visibleResults.Clear();
        bool appHeaderShown = false;
        bool fileHeaderShown = false;
        foreach (SearchResult item in matches)
        {
            if (item.ProviderId == "files") continue;
            bool firstApp = item.ProviderId == "apps" && !appHeaderShown;
            AddVisibleResult(item, firstApp ? "Apps" : null);
            if (firstApp) appHeaderShown = true;
        }
        foreach (SearchResult item in matches)
        {
            if (item.ProviderId != "files") continue;
            AddVisibleResult(item, fileHeaderShown ? null : "Files & Folders");
            fileHeaderShown = true;
        }
        _results.Visibility = _viewModel.IsResultsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (DiagnosticLog.Enabled) DiagnosticLog.Write($"ListView ItemsSource count: {matches.Count}");
        _results.SelectedIndex = matches.Count == 0 || _pomodoroViewModel.IsKeyboardSelected ? -1 : 0;
        int listHeight = Math.Min(_pomodoroPanel?.Visibility == Visibility.Visible ? 220 : 384, matches.Count * 48 +
            (appHeaderShown ? SectionHeaderHeight : 0) + (fileHeaderShown ? SectionHeaderHeight : 0));
        _results.Height = listHeight;
        int logicalHeight = 56 + (_pomodoroPanel?.Visibility == Visibility.Visible
            ? PomodoroVisuals.ControlsHeight : 0) + listHeight +
            (_error?.Visibility == Visibility.Visible ? 32 : 0);
        if (logicalHeight != _logicalHeight)
        {
            _logicalHeight = logicalHeight;
            if (_pomodoroPanel?.Visibility == Visibility.Visible || _timerLayoutChanged)
                OverlayWindow.Place(_window, logicalHeight);
            else OverlayWindow.ResizeHeight(_window, logicalHeight);
        }
        _timerLayoutChanged = false;
        if (PerfLog.Enabled && perfStart != 0)
            PerfLog.Record("query_update", perfStart, $"query={_input.Text.Replace('|', ' ')};results={matches.Count};index={_search.Count}");
    }

    private void AddVisibleResult(SearchResult item, string? sectionHeader)
    {
        if (_resultCache.Count >= 256) _resultCache.Clear();
        long iconLookup = PerfLog.Stamp();
        AppResultViewModel result;
        if (item.IconRef is null)
            result = new AppResultViewModel(item);
        else if (!_resultCache.TryGetValue(item.IconRef, out result!))
        {
            result = new AppResultViewModel(item);
            _resultCache.Add(item.IconRef, result);
            _icons?.Queue(result);
        }
        else if (PerfLog.Enabled && result.Icon is not null)
            PerfLog.Record("icon_cached", iconLookup, $"name={result.Name}");
        result.SectionHeader = sectionHeader;
        _visibleResults.Add(result);
    }

    private void OnHotkey(long stamp)
    {
        if (PerfLog.Enabled) PerfLog.Point("hotkey_received", $"visible={_window?.AppWindow.IsVisible};showing={_showing}");
        _pendingHotkeyFrame = stamp;
        Toggle();
        if (PerfLog.Enabled) PerfLog.Point("hotkey_toggled", $"visible={_window?.AppWindow.IsVisible};showing={_showing}");
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
        if (DateTimeOffset.UtcNow - _lastFilesReconciled >= TimeSpan.FromMinutes(15))
        {
            _lastFilesReconciled = DateTimeOffset.UtcNow;
            _fileIndexer?.RequestReconcile();
        }
        _showing = true;
        if (_fileIndexDirty)
        {
            _resultCache.Clear();
            _fileIndexDirty = false;
        }
        _activationGate.BeginShow();
        if (_input is not null) _input.Text = string.Empty;
        UpdateResults();
        OverlayWindow.Place(_window, _logicalHeight);
        _window.AppWindow.Show();
        OverlayWindow.RestoreBackdrop(_window);
        if (PerfLog.Enabled) PerfLog.Point("window_show", $"visible={_window.AppWindow.IsVisible}");
        OverlayWindow.BringToForeground(_window);
        PostUi(() =>
        {
            bool focused = _input?.Focus(FocusState.Programmatic) == true;
            nint foreground = ForegroundWindowHandle();
            nint own = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            if (foreground == own && focused) _activationGate.Activated();
            if (PerfLog.Enabled) PerfLog.Point("overlay_focus_ready",
                $"focused={focused};foreground={foreground};own={own};armed={_activationGate.ShouldHideOnDeactivated}");
            if (DiagnosticLog.Enabled) DiagnosticLog.Write($"TextBox focus after activation: {focused}");
        });
    }

    private void Hide()
    {
        if (PerfLog.Enabled) PerfLog.Point("window_hide", $"visible_before={_window?.AppWindow.IsVisible}");
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
        SyncPomodoroDisplayTimer();
    }

    private async Task ExecuteResultAsync(SearchResult result)
    {
        try
        {
            bool admin = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            await ResultActionExecutor.ExecuteAsync(result.Action, admin);
            if (DiagnosticLog.Enabled) DiagnosticLog.Write($"Executed result: {result.Title}, admin: {admin}");
            if (result.Action is ResultAction.LaunchApp launch)
            {
                _search.RecordLaunch(launch.Item, DateTimeOffset.UtcNow);
                _ = SaveUsageAsync(_search.SnapshotUsage());
            }
            if (_error is not null) _error.Visibility = Visibility.Collapsed;
            Hide();
        }
        catch (Exception error)
        {
            bool copying = result.Action is ResultAction.CopyText;
            AppLog.Error($"{(copying ? "Copy" : "Launch")} {result.Title}", error);
            if (_error is not null)
            {
                _error.Text = copying
                    ? $"Could not copy {result.Title}: {error.Message}"
                    : $"Could not open {result.Title}: {error.Message}";
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
            if (_pomodoroViewModel.IsKeyboardSelected)
            {
                _pomodoroViewModel.IsKeyboardSelected = false;
                _results.SelectedIndex = 0;
            }
            else _results.SelectedIndex = Math.Min(_results.SelectedIndex + 1, _results.Items.Count - 1);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Up && _results is not null && _results.Items.Count > 0)
        {
            if (_pomodoroPanel?.Visibility == Visibility.Visible && _results.SelectedIndex <= 0)
            {
                _results.SelectedIndex = -1;
                _pomodoroViewModel.IsKeyboardSelected = true;
            }
            else _results.SelectedIndex = Math.Max(_results.SelectedIndex - 1, 0);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter && _pomodoroViewModel.IsKeyboardSelected)
        {
            StartPausePomodoro();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter && _results?.SelectedItem is AppResultViewModel item)
        {
            _ = ExecuteResultAsync(item.Result);
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

    private static unsafe nint ForegroundWindowHandle() => (nint)PInvoke.GetForegroundWindow().Value;

    private async void ExitApp()
    {
        await SaveSettingsAsync();
        _stop.Cancel();
        StopPomodoroDisplayTimer();
        _pomodoroDeadline?.Cancel();
        _indexer?.Dispose();
        _fileIndexer?.Dispose();
        _icons?.Dispose();
        _systemEvents?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        Application.Current.Exit();
    }

    private void RunExceptionProbes()
    {
        PostUi(() => throw new InvalidOperationException("WinLight UI exception probe"));
        _ = Task.Run(async () =>
        {
            await Task.Delay(250).ConfigureAwait(false);
            try { throw new InvalidOperationException("WinLight background exception probe"); }
            catch (Exception error) { AppLog.Error("Background exception probe", error); }
        });
    }

    private async Task LoadUsageAsync()
    {
        try
        {
            Dictionary<string, UsageRecord> usage = await UsageStore.LoadAsync(UsageStore.DefaultPath, _stop.Token)
                .ConfigureAwait(false);
            PostUi(() => { _search.LoadUsage(usage); UpdateResults(); });
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
                    PostUi(Show);
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

    private void PostUi(Action action)
    {
        _dispatcher?.TryEnqueue(() =>
        {
            try { action(); }
            catch (Exception error) { AppLog.Error("UI dispatcher callback", error); }
        });
    }

}
