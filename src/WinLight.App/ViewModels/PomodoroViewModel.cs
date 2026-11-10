using CommunityToolkit.Mvvm.ComponentModel;
using WinLight.Core;

namespace WinLight.App.ViewModels;

public partial class PomodoroViewModel : ObservableObject
{
    [ObservableProperty] public partial string SessionLabel { get; set; } = "Focus";
    [ObservableProperty] public partial string RemainingText { get; set; } = "25:00";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial int CompletedFocusSessions { get; set; }
    [ObservableProperty] public partial string StatusLabel { get; set; } = "Ready";
    [ObservableProperty] public partial string RecommendedLabel { get; set; } = "";
    [ObservableProperty] public partial string StartPauseLabel { get; set; } = "Start";
    [ObservableProperty] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial bool IsKeyboardSelected { get; set; }
    [ObservableProperty] public partial bool AutoStartNextSession { get; set; }
    [ObservableProperty] public partial int FocusMinutes { get; set; } = 25;
    [ObservableProperty] public partial int ShortBreakMinutes { get; set; } = 5;
    [ObservableProperty] public partial int LongBreakMinutes { get; set; } = 15;

    public void Update(PomodoroTimer timer)
    {
        PomodoroState state = timer.State;
        TimeSpan remaining = timer.Remaining;
        SessionLabel = Label(state.Session);
        RemainingText = $"{(int)Math.Ceiling(remaining.TotalSeconds) / 60:00}:{(int)Math.Ceiling(remaining.TotalSeconds) % 60:00}";
        Progress = timer.Progress * 100;
        CompletedFocusSessions = state.CompletedFocusSessions;
        StatusLabel = state.Status.ToString();
        IsRunning = state.Status == PomodoroStatus.Running;
        StartPauseLabel = state.Status switch
        {
            PomodoroStatus.Running => "Pause",
            PomodoroStatus.Paused => "Resume",
            PomodoroStatus.Completed => $"Start {Label(state.RecommendedNext)}",
            _ => "Start"
        };
        RecommendedLabel = state.Status == PomodoroStatus.Completed
            ? $"Next: {Label(state.RecommendedNext)}" : "";
        FocusMinutes = timer.Durations.FocusMinutes;
        ShortBreakMinutes = timer.Durations.ShortBreakMinutes;
        LongBreakMinutes = timer.Durations.LongBreakMinutes;
    }

    private static string Label(PomodoroSession session) => session switch
    {
        PomodoroSession.Focus => "Focus",
        PomodoroSession.ShortBreak => "Short break",
        _ => "Long break"
    };
}
