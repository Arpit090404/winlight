using WinLight.Core;

namespace WinLight.Tests;

public sealed class PomodoroTimerTests
{
    private readonly FakeClock _clock = new();

    [Theory]
    [InlineData("pom", true)]
    [InlineData("POMODORO", true)]
    [InlineData(" tim ", true)]
    [InlineData("timer", true)]
    [InlineData("ti", false)]
    [InlineData("time zone", false)]
    [InlineData("pomelo", false)]
    [InlineData("calc", false)]
    public void LauncherTriggerMatchesOnlyTimerPrefixes(string query, bool expected) =>
        Assert.Equal(expected, PomodoroQuery.IsMatch(query));

    [Fact]
    public void DefaultsAndInitialState()
    {
        var timer = NewTimer();
        Assert.Equal(PomodoroSession.Focus, timer.State.Session);
        Assert.Equal(PomodoroStatus.Ready, timer.State.Status);
        Assert.Equal(TimeSpan.FromMinutes(25), timer.Remaining);
        Assert.Equal(0, timer.State.CompletedFocusSessions);
    }

    [Fact]
    public void StartPauseResumeUsesDeadlineWithoutDrift()
    {
        var timer = NewTimer();
        timer.Start();
        DateTimeOffset deadline = timer.State.DeadlineUtc!.Value;
        timer.Start();
        Assert.Equal(deadline, timer.State.DeadlineUtc);
        _clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(8));
        Assert.Equal(TimeSpan.FromMinutes(20) + TimeSpan.FromSeconds(52), timer.Remaining);
        timer.Pause();
        _clock.Advance(TimeSpan.FromHours(10));
        Assert.Equal(TimeSpan.FromMinutes(20) + TimeSpan.FromSeconds(52), timer.Remaining);
        timer.Start();
        Assert.Equal(_clock.GetUtcNow() + timer.Remaining, timer.State.DeadlineUtc);
    }

    [Fact]
    public void DelayedRefreshCompletesOnceAndOffersBreak()
    {
        var timer = NewTimer();
        timer.Start();
        _clock.Advance(TimeSpan.FromHours(8));
        Assert.True(timer.Refresh());
        Assert.False(timer.Refresh());
        Assert.Equal(PomodoroStatus.Completed, timer.State.Status);
        Assert.Equal(1, timer.State.CompletedFocusSessions);
        Assert.Equal(PomodoroSession.ShortBreak, timer.State.RecommendedNext);
        Assert.Equal(1, timer.State.CompletionId);
        Assert.Equal(TimeSpan.Zero, timer.Remaining);
        timer.Start();
        Assert.Equal(PomodoroSession.ShortBreak, timer.State.Session);
        Assert.Equal(PomodoroStatus.Running, timer.State.Status);
    }

    [Fact]
    public void FourFocusSessionsOfferLongBreakThenResetCount()
    {
        var timer = NewTimer();
        for (int i = 1; i <= 4; i++)
        {
            timer.Start();
            _clock.Advance(TimeSpan.FromMinutes(25));
            Assert.Equal(i, timer.State.CompletedFocusSessions);
            Assert.Equal(i == 4 ? PomodoroSession.LongBreak : PomodoroSession.ShortBreak,
                timer.State.RecommendedNext);
            timer.Start();
            _clock.Advance(TimeSpan.FromMinutes(i == 4 ? 15 : 5));
            Assert.Equal(PomodoroSession.Focus, timer.State.RecommendedNext);
            Assert.Equal(i == 4 ? 0 : i, timer.State.CompletedFocusSessions);
        }
    }

    [Fact]
    public void ResetAndSkipDoNotCreditFocus()
    {
        var timer = NewTimer();
        timer.Start();
        _clock.Advance(TimeSpan.FromMinutes(3));
        timer.Reset();
        Assert.Equal(TimeSpan.FromMinutes(25), timer.Remaining);
        Assert.Equal(PomodoroStatus.Ready, timer.State.Status);
        timer.Skip();
        Assert.Equal(PomodoroSession.ShortBreak, timer.State.Session);
        Assert.Equal(0, timer.State.CompletedFocusSessions);
        timer.Skip();
        Assert.Equal(PomodoroSession.Focus, timer.State.Session);
    }

    [Fact]
    public void SelectionPreventsTwoActiveSessions()
    {
        var timer = NewTimer();
        timer.Select(PomodoroSession.LongBreak);
        Assert.Equal(TimeSpan.FromMinutes(15), timer.Remaining);
        timer.Start();
        Assert.Throws<InvalidOperationException>(() => timer.Select(PomodoroSession.Focus));
        Assert.Equal(PomodoroSession.LongBreak, timer.State.Session);
        timer.Pause();
        timer.Select(PomodoroSession.Focus);
        Assert.Equal(PomodoroStatus.Ready, timer.State.Status);
    }

    [Fact]
    public void DurationsValidateAndUpdateReadySession()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PomodoroTimer(_clock,
            new PomodoroDurations(0, 5, 15)));
        var timer = NewTimer();
        timer.Configure(new PomodoroDurations(30, 7, 20));
        Assert.Equal(TimeSpan.FromMinutes(30), timer.Remaining);
        timer.Select(PomodoroSession.ShortBreak);
        Assert.Equal(TimeSpan.FromMinutes(7), timer.Remaining);
        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Configure(
            new PomodoroDurations(30, 181, 20)));
    }

    [Fact]
    public async Task RunningAndPausedSessionsRecoverFromSettingsDocument()
    {
        string path = Path.Combine(Path.GetTempPath(), $"WinLight.Pomodoro.{Guid.NewGuid():N}.json");
        try
        {
            var timer = NewTimer();
            timer.Start();
            await SettingsStore.SaveAsync(path, new WinLightSettings
            {
                PomodoroDurations = timer.Durations, PomodoroState = timer.State
            });
            _clock.Advance(TimeSpan.FromMinutes(10));
            WinLightSettings saved = await SettingsStore.LoadAsync(path);
            var restored = new PomodoroTimer(_clock, saved.PomodoroDurations, saved.PomodoroState);
            Assert.Equal(TimeSpan.FromMinutes(15), restored.Remaining);
            restored.Pause();
            await SettingsStore.SaveAsync(path, saved with { PomodoroState = restored.State });
            _clock.Advance(TimeSpan.FromHours(2));
            saved = await SettingsStore.LoadAsync(path);
            restored = new PomodoroTimer(_clock, saved.PomodoroDurations, saved.PomodoroState);
            Assert.Equal(TimeSpan.FromMinutes(15), restored.Remaining);
            Assert.Equal(PomodoroStatus.Paused, restored.State.Status);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RecoveryAfterDeadlineCompletesWithoutStartingNextSession()
    {
        var timer = NewTimer();
        timer.Start();
        PomodoroState snapshot = timer.State;
        _clock.Advance(TimeSpan.FromDays(1));
        var restored = new PomodoroTimer(_clock, timer.Durations, snapshot);
        Assert.Equal(PomodoroStatus.Completed, restored.State.Status);
        Assert.Equal(PomodoroSession.Focus, restored.State.Session);
        Assert.Equal(PomodoroSession.ShortBreak, restored.State.RecommendedNext);
    }

    [Fact]
    public void CorruptStateFallsBackToReady()
    {
        var invalid = new PomodoroState(Status: PomodoroStatus.Running,
            Remaining: TimeSpan.FromMinutes(2));
        var timer = new PomodoroTimer(_clock, state: invalid);
        Assert.Equal(PomodoroStatus.Ready, timer.State.Status);
        Assert.Equal(TimeSpan.FromMinutes(25), timer.Remaining);
    }

    private PomodoroTimer NewTimer() => new(_clock);

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
