using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Win32;
using WinLight.Core;
using Windows.System;
using Windows.UI.Core;

namespace WinLight.App;

public sealed partial class App
{
    private WinLightSettings _settings = new();
    private Window? _settingsWindow;
    private TextBox? _hotkeyCapture;
    private ToggleSwitch? _startupToggle;
    private TextBlock? _settingsMessage;

    partial void ConfigureSettingsVisuals(StackPanel root, TextBox hotkeyCapture,
        ToggleSwitch startupToggle, Button rescanButton, TextBlock message);

    private async Task LoadSettingsAndRegisterAsync()
    {
        try
        {
            WinLightSettings settings = await SettingsStore.LoadAsync(SettingsStore.DefaultPath, _stop.Token)
                .ConfigureAwait(false);
            PostUi(() =>
            {
                _settings = settings;
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

    private void ShowSettings()
    {
        if (PerfLog.Enabled) PerfLog.Point("settings_show_begin",
            $"created={_settingsWindow is not null};thread={Environment.CurrentManagedThreadId}");
        if (_settingsWindow is null)
        {
            _hotkeyCapture = new TextBox
            {
                Header = "Hotkey (press a new combination)",
                Text = _settings.Hotkey.ToString(),
                IsReadOnly = true
            };
            _hotkeyCapture.Loaded += (_, _) => PostUi(() => _hotkeyCapture?.Focus(FocusState.Keyboard));
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
            var root = new StackPanel { Spacing = 12, Padding = new Thickness(16) };
            root.Children.Add(_hotkeyCapture);
            root.Children.Add(_startupToggle);
            root.Children.Add(rescan);
            root.Children.Add(_settingsMessage);
            ConfigureSettingsVisuals(root, _hotkeyCapture, _startupToggle, rescan, _settingsMessage);
            if (PerfLog.Enabled) PerfLog.Point("settings_visuals_ready", "created=true");
            _settingsWindow = new Window { Title = "WinLight Settings", Content = root };
            _settingsWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(440, 260));
            _settingsWindow.Activated += (_, e) =>
            {
                if (e.WindowActivationState != WindowActivationState.Deactivated)
                    PostUi(() => _hotkeyCapture?.Focus(FocusState.Programmatic));
            };
            _settingsWindow.AppWindow.Closing += (_, e) =>
            {
                e.Cancel = true;
                _settingsWindow.AppWindow.Hide();
            };
        }
        _hotkeyCapture!.Text = _settings.Hotkey.ToString();
        _startupToggle!.IsOn = _settings.StartAtLogin;
        _settingsWindow.AppWindow.Show();
        _settingsWindow.Activate();
        if (PerfLog.Enabled) PerfLog.Point("settings_show_end",
            $"visible={_settingsWindow.AppWindow.IsVisible};thread={Environment.CurrentManagedThreadId}");
        PostUi(() => _hotkeyCapture.Focus(FocusState.Programmatic));
    }

    private void OnHotkeyCapture(object sender, KeyRoutedEventArgs args)
    {
        if (PerfLog.Enabled) PerfLog.Point("settings_key_down",
            $"key={args.Key};focus={_hotkeyCapture?.FocusState}");
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
            $"settings_visible={_settingsWindow?.AppWindow.IsVisible};tray_created={_tray?.IsCreated}");
    }

    private void SetSettingsMessage(string message)
    {
        if (_settingsMessage is not null) _settingsMessage.Text = message;
    }
}
