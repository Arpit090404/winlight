using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Win32;
using WinLight.App.Native;
using WinLight.Core;
using Windows.System;
using Windows.UI.Core;

namespace WinLight.App;

public sealed partial class App
{
    private WinLightSettings _settings = new();
    private StackPanel? _settingsPanel;
    private TextBox? _hotkeyCapture;
    private ToggleSwitch? _startupToggle;
    private TextBlock? _settingsMessage;

    partial void ConfigureSettingsVisuals(StackPanel root, TextBox hotkeyCapture,
        ToggleSwitch startupToggle, Button rescanButton, TextBlock message, Action returnToSearch);
    private partial void FocusSettingsNavigation();

    private async Task LoadSettingsAndRegisterAsync()
    {
        try
        {
            WinLightSettings settings = await SettingsStore.LoadAsync(SettingsStore.DefaultPath, _stop.Token)
                .ConfigureAwait(false);
            PostUi(() =>
            {
                _settings = settings;
                InitializePomodoro(settings);
                if (Environment.GetEnvironmentVariable("WINLIGHT_TEST_HOTKEY_CONFLICT") == "1")
                {
                    bool held = _systemEvents?.HoldDefaultHotkeyForTest() == true;
                    if (PerfLog.Enabled) PerfLog.Point("hotkey_conflict_holder", $"held={held}");
                }
                if (Environment.GetEnvironmentVariable("WINLIGHT_TEST_HOTKEY_ROLLBACK") == "1")
                {
                    bool held = _systemEvents?.HoldReplacementHotkeyForTest() == true;
                    if (PerfLog.Enabled) PerfLog.Point("hotkey_replacement_holder", $"held={held}");
                }
                bool registered = _hotkey?.Register(settings.Hotkey) == true;
                if (PerfLog.Enabled) PerfLog.Point("hotkey_initial_register",
                    $"binding={settings.Hotkey};success={registered}");
                if (!registered)
                    NotifyHotkeyConflict($"{settings.Hotkey} is unavailable. Choose another hotkey in Settings.");
                if (registered && Environment.GetEnvironmentVariable("WINLIGHT_TEST_HOTKEY_ROLLBACK") == "1")
                {
                    bool changed = _hotkey?.TryChange(new HotkeyBinding(
                        HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4B)) == true;
                    if (!changed) NotifyHotkeyConflict("Ctrl+Alt+K is unavailable. Keeping Alt+Space.");
                    if (PerfLog.Enabled) PerfLog.Point("hotkey_rollback_probe",
                        $"changed={changed};registered={_hotkey?.IsRegistered};binding={_hotkey?.Binding}");
                }
                _ = ApplyStartupRegistrationAsync(settings.StartAtLogin);
            });
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error)
        {
            AppLog.Error("Load settings", error);
            PostUi(() =>
            {
                if (_hotkey?.Register(HotkeyBinding.Default) != true)
                    NotifyHotkeyConflict("Alt+Space is unavailable. Choose another hotkey in Settings.");
            });
        }
    }

    private void InitializeSettingsContent(StackPanel launcher)
    {
        _hotkeyCapture = new TextBox
        {
            Header = "Hotkey (press a new combination)",
            Text = _settings.Hotkey.ToString(),
            IsReadOnly = true
        };
        _hotkeyCapture.KeyDown += OnHotkeyCapture;
        _startupToggle = new ToggleSwitch
        {
            Header = "Start at login", IsOn = _settings.StartAtLogin
        };
        _startupToggle.Toggled += OnStartupToggled;
        var rescan = new Button { Content = "Rescan apps" };
        rescan.Click += (_, _) =>
        {
            _indexer?.Refresh();
            SetSettingsMessage("Rescanning apps in the background.");
        };
        _settingsMessage = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _settingsPanel = new StackPanel { Visibility = Visibility.Collapsed };
        _settingsPanel.Children.Add(_hotkeyCapture);
        _settingsPanel.Children.Add(_startupToggle);
        _settingsPanel.Children.Add(rescan);
        _settingsPanel.Children.Add(_settingsMessage);
        ConfigureSettingsVisuals(_settingsPanel, _hotkeyCapture, _startupToggle, rescan,
            _settingsMessage, ReturnToSearch);
        launcher.Children.Insert(2, _settingsPanel);
    }

    private void ShowSettings() => ShowSettings(fromQuery: false);

    private void ShowSettings(bool fromQuery)
    {
        if (_settingsPanel is null || _input is null || _window is null) return;
        if (!_showing) Show();
        if (_navigation.IsSettingsOpen) return;
        if (PerfLog.Enabled) PerfLog.Point("settings_show_begin",
            $"thread={Environment.CurrentManagedThreadId}");
        _navigation.EnterSettings(_input.Text, fromQuery);
        _hotkeyCapture!.Text = _settings.Hotkey.ToString();
        _startupToggle!.IsOn = _settings.StartAtLogin;
        if (_error is not null) _searchErrorVisibility = _error.Visibility;
        ++_resultQueryId;
        _engine.CancelCurrentQuery();
        if (_results is not null) _results.Visibility = Visibility.Collapsed;
        if (_pomodoroPanel is not null) _pomodoroPanel.Visibility = Visibility.Collapsed;
        if (_error is not null) _error.Visibility = Visibility.Collapsed;
        SyncPomodoroDisplayTimer();
        _settingsPanel.Visibility = Visibility.Visible;
        _logicalHeight = SettingsHeight;
        OverlayWindow.Place(_window, SettingsHeight);
        if (PerfLog.Enabled) PerfLog.Point("settings_show_end",
            $"visible={_window.AppWindow.IsVisible};thread={Environment.CurrentManagedThreadId}");
        PostUi(() =>
        {
            if (_showing && _navigation.IsSettingsOpen) FocusSettingsNavigation();
        });
    }

    private void ReturnToSearch() => ReturnToSearch(restoreQuery: true, focusSearch: true);

    private void ReturnToSearch(bool restoreQuery, bool focusSearch)
    {
        if (!_navigation.IsSettingsOpen || _input is null || _window is null) return;
        string query = _navigation.ReturnToSearch();
        if (restoreQuery && _input.Text != query)
        {
            _restoringSearchQuery = true;
            try { _input.Text = query; }
            finally { _restoringSearchQuery = false; }
            _searchResultsDirty = true;
        }
        if (_settingsPanel is not null) _settingsPanel.Visibility = Visibility.Collapsed;
        RestoreSearchContent();
        if (focusSearch) PostUi(() => _input.Focus(FocusState.Programmatic));
    }

    private void RestoreSearchContent()
    {
        if (_navigation.IsSettingsOpen || _input is null || _window is null) return;
        if (_settingsPanel is not null) _settingsPanel.Visibility = Visibility.Collapsed;
        if (_results is not null)
            _results.Visibility = _viewModel.IsResultsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (_error is not null) _error.Visibility = _searchErrorVisibility;
        if (_pomodoroPanel is not null)
            _pomodoroPanel.Visibility = PomodoroQuery.IsMatch(_input.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        _logicalHeight = _searchHeight;
        OverlayWindow.Place(_window, _searchHeight);
        SyncPomodoroDisplayTimer();
        if (_searchResultsDirty)
        {
            _searchResultsDirty = false;
            UpdateResults();
        }
    }

    private void OnHotkeyCapture(object sender, KeyRoutedEventArgs args)
    {
        if (PerfLog.Enabled) PerfLog.Point("settings_key_down",
            $"key={args.Key};focus={_hotkeyCapture?.FocusState}");
        if (args.Key == VirtualKey.Escape) return;
        int key = (int)args.Key;
        if (key is 0x09 or 0x10 or 0x11 or 0x12 or 0x5B or 0x5C) return;
        HotkeyModifiers modifiers = HotkeyModifiers.None;
        if (IsDown(VirtualKey.Control)) modifiers |= HotkeyModifiers.Control;
        if (IsDown(VirtualKey.Menu)) modifiers |= HotkeyModifiers.Alt;
        if (IsDown(VirtualKey.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) modifiers |= HotkeyModifiers.Windows;
        var candidate = new HotkeyBinding(modifiers, key);
        args.Handled = true;
        if (!candidate.IsValid(out string reason))
        {
            SetSettingsMessage(reason);
            return;
        }
        if (_hotkey?.TryChange(candidate) != true)
        {
            NotifyHotkeyConflict($"{candidate} is unavailable. Keeping {_settings.Hotkey}.");
            _hotkeyCapture!.Text = _settings.Hotkey.ToString();
            return;
        }
        _settings = _settings with { Hotkey = candidate };
        _hotkeyCapture!.Text = candidate.ToString();
        SetSettingsMessage($"Hotkey changed to {candidate}.");
        _ = SaveSettingsAsync();
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(CoreVirtualKeyStates.Down);

    private void OnStartupToggled(object sender, RoutedEventArgs args)
    {
        if (_startupToggle is null) return;
        _settings = _settings with { StartAtLogin = _startupToggle.IsOn };
        _ = ApplyStartupRegistrationAsync(_settings.StartAtLogin);
        _ = SaveSettingsAsync();
    }

    private async Task SaveSettingsAsync()
    {
        try { await SettingsStore.SaveAsync(SettingsStore.DefaultPath, _settings, _stop.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { AppLog.Error("Save settings", error); }
    }

    private async Task ApplyStartupRegistrationAsync(bool enabled)
    {
        try
        {
            await Task.Run(() =>
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (enabled) key.SetValue("WinLight", $"\"{Environment.ProcessPath}\"");
                else key.DeleteValue("WinLight", throwOnMissingValue: false);
            }, _stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { AppLog.Error("Update login startup", error); }
    }

    private void OnTaskbarCreated()
    {
        if (PerfLog.Enabled) PerfLog.Point("taskbar_created", "broadcast_received=true");
        // H.NotifyIcon also handles TaskbarCreated internally; force creation is a recovery path if it did not.
        try
        {
            _tray?.ForceCreate(false);
            if (PerfLog.Enabled) PerfLog.Point("tray_recreated", $"is_created={_tray?.IsCreated}");
        }
        catch (Exception error) { AppLog.Error("Restore tray icon", error); }
    }

    private void OnSessionResumed(string reason)
    {
        if (PerfLog.Enabled) PerfLog.Point("session_resume", $"reason={reason}");
        ReconcilePomodoro();
        SchedulePomodoroDeadline();
        try
        {
            bool registered = _hotkey?.Restore() == true;
            if (PerfLog.Enabled) PerfLog.Point("hotkey_reregister", $"success={registered}");
            if (!registered)
                NotifyHotkeyConflict($"{_settings.Hotkey} is unavailable after resume. Choose another hotkey.");
        }
        catch (Exception error) { AppLog.Error("Restore hotkey after resume", error); }
    }

    private void NotifyHotkeyConflict(string message)
    {
        AppLog.Error("Register hotkey", new InvalidOperationException(message));
        try
        {
            if (_tray?.IsCreated == true) _tray.ShowNotification("WinLight hotkey", message);
        }
        catch (Exception error) { AppLog.Error("Show hotkey notification", error); }
        ShowSettings();
        SetSettingsMessage(message);
        if (PerfLog.Enabled) PerfLog.Point("hotkey_conflict_fallback",
            $"settings_visible={_navigation.IsSettingsOpen && _window?.AppWindow.IsVisible == true};tray_created={_tray?.IsCreated}");
    }

    private void SetSettingsMessage(string message)
    {
        if (_settingsMessage is not null) _settingsMessage.Text = message;
    }
}
