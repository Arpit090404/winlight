namespace WinLight.Core;

[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }

public readonly record struct HotkeyBinding(HotkeyModifiers Modifiers, int KeyCode)
{
    public static HotkeyBinding Default => new(HotkeyModifiers.Alt, 0x20);

    public bool IsValid(out string error)
    {
        if (Modifiers == HotkeyModifiers.None || (Modifiers & ~(HotkeyModifiers.Alt |
            HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Windows)) != 0)
        { error = "Choose at least one modifier key."; return false; }
        if (KeyCode is < 0x08 or > 0xFE or 0x10 or 0x11 or 0x12 or 0x5B or 0x5C)
        { error = "Choose a non-modifier key."; return false; }
        if (Modifiers.HasFlag(HotkeyModifiers.Windows) ||
            (Modifiers.HasFlag(HotkeyModifiers.Alt) && KeyCode is 0x09 or 0x73 or 0x1B) ||
            (Modifiers.HasFlag(HotkeyModifiers.Control) && Modifiers.HasFlag(HotkeyModifiers.Alt) && KeyCode == 0x2E) ||
            (Modifiers.HasFlag(HotkeyModifiers.Control) && KeyCode == 0x1B))
        { error = "Windows reserves that key combination."; return false; }
        error = string.Empty;
        return true;
    }

    public override string ToString()
    {
        string key = KeyCode switch
        {
            0x20 => "Space",
            >= 0x70 and <= 0x87 => $"F{KeyCode - 0x6F}",
            >= 0x30 and <= 0x5A => ((char)KeyCode).ToString(),
            _ => $"VK {KeyCode:X2}"
        };
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(key);
        return string.Join("+", parts);
    }
}

public sealed record WinLightSettings
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public HotkeyBinding Hotkey { get; init; } = HotkeyBinding.Default;
    public bool StartAtLogin { get; init; } = true;
    public PomodoroDurations PomodoroDurations { get; init; } = new();
    public PomodoroState? PomodoroState { get; init; }
    public bool FloatingTimerDismissed { get; init; }
    public bool AutoStartNextSession { get; init; }
}
