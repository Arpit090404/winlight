namespace WinLight.Core;

public enum PomodoroSession { Focus, ShortBreak, LongBreak }
public enum PomodoroStatus { Ready, Running, Paused, Completed }

public sealed record PomodoroDurations(int FocusMinutes = 25, int ShortBreakMinutes = 5,
    int LongBreakMinutes = 15)
{
    public bool IsValid => FocusMinutes is >= 1 and <= 180 &&
        ShortBreakMinutes is >= 1 and <= 180 && LongBreakMinutes is >= 1 and <= 180;

    public TimeSpan For(PomodoroSession session) => TimeSpan.FromMinutes(session switch
    {
        PomodoroSession.Focus => FocusMinutes,
        PomodoroSession.ShortBreak => ShortBreakMinutes,
        _ => LongBreakMinutes
    });
}

public sealed record PomodoroState(
    PomodoroSession Session = PomodoroSession.Focus,
    PomodoroStatus Status = PomodoroStatus.Ready,
    TimeSpan Remaining = default,
    DateTimeOffset? DeadlineUtc = null,
    int CompletedFocusSessions = 0,
    PomodoroSession RecommendedNext = PomodoroSession.Focus,
    long CompletionId = 0);

public sealed class PomodoroTimer
{
    private readonly TimeProvider _clock;
    private PomodoroDurations _durations;
    private PomodoroState _state;

    public PomodoroTimer(TimeProvider? clock = null, PomodoroDurations? durations = null,
        PomodoroState? state = null)
    {
        _clock = clock ?? TimeProvider.System;
        _durations = durations ?? new PomodoroDurations();
        if (!_durations.IsValid) throw new ArgumentOutOfRangeException(nameof(durations));
        _state = Validate(state) ? state! : new PomodoroState(Remaining: _durations.For(PomodoroSession.Focus));
        Refresh();
    }

    public PomodoroDurations Durations => _durations;
    public PomodoroState State { get { Refresh(); return _state; } }
    public TimeSpan Remaining
    {
        get
        {
            Refresh();
            return _state.Status == PomodoroStatus.Running
                ? MaxZero(_state.DeadlineUtc!.Value - _clock.GetUtcNow()) : _state.Remaining;
        }
    }
    public double Progress => Math.Clamp(1 - Remaining.TotalSeconds /
        _durations.For(_state.Session).TotalSeconds, 0, 1);

    public void Configure(PomodoroDurations durations)
    {
        if (!durations.IsValid) throw new ArgumentOutOfRangeException(nameof(durations));
        Refresh();
        _durations = durations;
        if (_state.Status is PomodoroStatus.Ready or PomodoroStatus.Completed)
            _state = _state with { Remaining = durations.For(_state.Session) };
    }

    public void Select(PomodoroSession session)
    {
        Refresh();
        if (_state.Status == PomodoroStatus.Running)
            throw new InvalidOperationException("Pause, reset, or skip the active session first.");
        _state = _state with { Session = session, Status = PomodoroStatus.Ready,
            Remaining = _durations.For(session), DeadlineUtc = null };
    }

    public void Start()
    {
        Refresh();
        if (_state.Status == PomodoroStatus.Running) return;
        if (_state.Status == PomodoroStatus.Completed)
            Select(_state.RecommendedNext);
        TimeSpan remaining = _state.Status == PomodoroStatus.Ready
            ? _durations.For(_state.Session) : _state.Remaining;
        _state = _state with { Status = PomodoroStatus.Running, Remaining = remaining,
            DeadlineUtc = _clock.GetUtcNow() + remaining };
    }

    public void Pause()
    {
        Refresh();
        if (_state.Status != PomodoroStatus.Running) return;
        _state = _state with { Status = PomodoroStatus.Paused,
            Remaining = MaxZero(_state.DeadlineUtc!.Value - _clock.GetUtcNow()), DeadlineUtc = null };
    }

    public void Reset()
    {
        Refresh();
        _state = _state with { Status = PomodoroStatus.Ready,
            Remaining = _durations.For(_state.Session), DeadlineUtc = null };
    }

    public void Skip()
    {
        Refresh();
        PomodoroSession next = _state.Session == PomodoroSession.Focus
            ? BreakAfterFocus() : PomodoroSession.Focus;
        _state = _state with { Session = next, RecommendedNext = next,
            Status = PomodoroStatus.Ready, Remaining = _durations.For(next), DeadlineUtc = null };
    }

    public bool Refresh()
    {
        if (_state.Status != PomodoroStatus.Running ||
            _clock.GetUtcNow() < _state.DeadlineUtc) return false;
        int count = _state.CompletedFocusSessions;
        PomodoroSession next;
        if (_state.Session == PomodoroSession.Focus)
        {
            count++;
            next = count % 4 == 0 ? PomodoroSession.LongBreak : PomodoroSession.ShortBreak;
        }
        else
        {
            if (_state.Session == PomodoroSession.LongBreak) count = 0;
            next = PomodoroSession.Focus;
        }
        _state = _state with { Status = PomodoroStatus.Completed, Remaining = TimeSpan.Zero,
            DeadlineUtc = null, CompletedFocusSessions = count, RecommendedNext = next,
            CompletionId = _state.CompletionId + 1 };
        return true;
    }

    private PomodoroSession BreakAfterFocus() => _state.CompletedFocusSessions > 0 &&
        _state.CompletedFocusSessions % 4 == 0
        ? PomodoroSession.LongBreak : PomodoroSession.ShortBreak;
    private static TimeSpan MaxZero(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
    private static bool Validate(PomodoroState? state) => state is not null &&
        Enum.IsDefined(state.Session) && Enum.IsDefined(state.Status) &&
        Enum.IsDefined(state.RecommendedNext) && state.CompletedFocusSessions >= 0 &&
        state.Remaining >= TimeSpan.Zero &&
        (state.Status == PomodoroStatus.Running) == state.DeadlineUtc.HasValue;
}
