using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinLight.App.Native;
using WinLight.App.ViewModels;
using WinLight.Core;
using Windows.Graphics;

namespace WinLight.App;

public sealed partial class App
{
    private PomodoroTimer _pomodoro = new();
    private readonly PomodoroViewModel _pomodoroViewModel = new();
    private Window? _floatingTimerWindow;
    private DispatcherQueueTimer? _pomodoroDisplayTimer;
    private CancellationTokenSource? _pomodoroDeadline;
    private long _notifiedCompletionId;

    private void InitializePomodoro(WinLightSettings settings)
    {
        _notifiedCompletionId = settings.PomodoroState?.CompletionId ?? 0;
        _pomodoroViewModel.AutoStartNextSession = settings.AutoStartNextSession;
        _pomodoro = new PomodoroTimer(durations: settings.PomodoroDurations,
            state: settings.PomodoroState);
        if (_pomodoro.State.Status is PomodoroStatus.Running or PomodoroStatus.Paused or PomodoroStatus.Completed &&
            !settings.FloatingTimerDismissed)
            EnsureFloatingWindow();
        ReconcilePomodoro();
        SchedulePomodoroDeadline();
    }

    private void EnsureFloatingWindow()
    {
        if (_floatingTimerWindow is not null) return;
        _pomodoroViewModel.Update(_pomodoro);
        _floatingTimerWindow = new Window
        {
            Title = "WinLight Timer",
            Content = PomodoroVisuals.CreateFloating(_pomodoroViewModel,
                ShowPomodoroControls, DismissFloatingTimer)
        };
        _floatingTimerWindow.ExtendsContentIntoTitleBar = true;
        if (PomodoroVisuals.FloatingDragRegion is { } drag)
            _floatingTimerWindow.SetTitleBar(drag);
        OverlayWindow.Configure(_floatingTimerWindow);
        _floatingTimerWindow.AppWindow.Resize(new SizeInt32(
            PomodoroVisuals.FloatingWidth, PomodoroVisuals.FloatingHeight));
        _floatingTimerWindow.AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            DismissFloatingTimer();
        };
        _floatingTimerWindow.AppWindow.Hide();
    }

    private void ShowPomodoroControls()
    {
        ReconcilePomodoro();
        if (!_showing) Show();
        if (_input is not null) _input.Text = "pomodoro";
        SyncPomodoroDisplayTimer();
    }

    private void SelectPomodoro(PomodoroSession session) => ChangePomodoro(() => _pomodoro.Select(session));
    private void StartPausePomodoro() => ChangePomodoro(() =>
    {
        if (_pomodoro.State.Status == PomodoroStatus.Running) _pomodoro.Pause();
        else _pomodoro.Start();
        if (_pomodoro.State.Status == PomodoroStatus.Running)
        {
            _settings = _settings with { FloatingTimerDismissed = false };
            EnsureFloatingWindow();
        }
    });
    private void ResetPomodoro() => ChangePomodoro(_pomodoro.Reset);
    private void SkipPomodoro() => ChangePomodoro(_pomodoro.Skip);
    private void ConfigurePomodoro(PomodoroDurations durations) => ChangePomodoro(() =>
    {
        _pomodoro.Configure(durations);
        _settings = _settings with { PomodoroDurations = durations };
    });

    private void ConfigurePomodoroAutoStart(bool enabled)
    {
        _settings = _settings with { AutoStartNextSession = enabled };
        _pomodoroViewModel.AutoStartNextSession = enabled;
        _ = SaveSettingsAsync();
    }

    private void ChangePomodoro(Action change)
    {
        try
        {
            change();
            ReconcilePomodoro();
            SchedulePomodoroDeadline();
            _ = SaveSettingsAsync();
        }
        catch (Exception error) { AppLog.Error("Change timer session", error); }
    }

    private void DismissFloatingTimer()
    {
        _floatingTimerWindow?.AppWindow.Hide();
        _settings = _settings with { FloatingTimerDismissed = true };
        _ = SaveSettingsAsync();
        SyncPomodoroDisplayTimer();
    }

    private void ReconcilePomodoro()
    {
        PomodoroState state = _pomodoro.State;
        _pomodoroViewModel.Update(_pomodoro);
        if (state.CompletionId > _notifiedCompletionId)
        {
            _notifiedCompletionId = state.CompletionId;
            try
            {
                _tray?.ShowNotification("WinLight Timer",
                    $"{_pomodoroViewModel.SessionLabel} complete. {_pomodoroViewModel.RecommendedLabel}.");
            }
            catch (Exception error) { AppLog.Error("Show timer completion notification", error); }
            if (_settings.AutoStartNextSession)
            {
                _pomodoro.Start();
                state = _pomodoro.State;
                _pomodoroViewModel.Update(_pomodoro);
                SchedulePomodoroDeadline();
            }
            _settings = _settings with { PomodoroState = state };
            _ = SaveSettingsAsync();
        }
        else _settings = _settings with { PomodoroState = state };
        if (_floatingTimerWindow is not null)
        {
            bool visible = state.Status is PomodoroStatus.Running or PomodoroStatus.Paused or PomodoroStatus.Completed &&
                !_settings.FloatingTimerDismissed;
            if (visible && !_floatingTimerWindow.AppWindow.IsVisible)
            {
                _floatingTimerWindow.AppWindow.Show();
                OverlayWindow.RestoreBackdrop(_floatingTimerWindow);
            }
            else if (!visible && _floatingTimerWindow.AppWindow.IsVisible)
                _floatingTimerWindow.AppWindow.Hide();
        }
        SyncPomodoroDisplayTimer();
    }

    private void SchedulePomodoroDeadline()
    {
        _pomodoroDeadline?.Cancel();
        _pomodoroDeadline?.Dispose();
        _pomodoroDeadline = null;
        PomodoroState state = _pomodoro.State;
        if (state.Status != PomodoroStatus.Running) return;
        var source = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _pomodoroDeadline = source;
        _ = WaitForPomodoroDeadlineAsync(state.DeadlineUtc!.Value, source.Token);
    }

    private async Task WaitForPomodoroDeadlineAsync(DateTimeOffset deadline, CancellationToken stop)
    {
        try
        {
            TimeSpan delay = deadline - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, stop).ConfigureAwait(false);
            PostUi(ReconcilePomodoro);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) { AppLog.Error("Timer completion wait", error); }
    }

    private void SyncPomodoroDisplayTimer()
    {
        bool visible = (_showing && _pomodoroPanel?.Visibility == Visibility.Visible) ||
            _floatingTimerWindow?.AppWindow.IsVisible == true;
        if (!visible || _pomodoro.State.Status != PomodoroStatus.Running)
        {
            StopPomodoroDisplayTimer();
            return;
        }
        _pomodoroDisplayTimer ??= _dispatcher!.CreateTimer();
        _pomodoroDisplayTimer.Interval = TimeSpan.FromSeconds(1);
        if (!_pomodoroDisplayTimer.IsRunning)
        {
            _pomodoroDisplayTimer.Tick += OnPomodoroDisplayTick;
            _pomodoroDisplayTimer.Start();
        }
    }

    private void OnPomodoroDisplayTick(DispatcherQueueTimer sender, object args) => ReconcilePomodoro();

    private void StopPomodoroDisplayTimer()
    {
        if (_pomodoroDisplayTimer?.IsRunning != true) return;
        _pomodoroDisplayTimer.Stop();
        _pomodoroDisplayTimer.Tick -= OnPomodoroDisplayTick;
    }
}
