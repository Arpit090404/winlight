namespace WinLight.Core;

public static class PomodoroQuery
{
    public static bool IsMatch(string query)
    {
        ReadOnlySpan<char> text = query.AsSpan().Trim();
        if (text.Length < 3) return false;
        return "pomodoro".AsSpan().StartsWith(text, StringComparison.OrdinalIgnoreCase) ||
            "timer".AsSpan().StartsWith(text, StringComparison.OrdinalIgnoreCase);
    }
}
