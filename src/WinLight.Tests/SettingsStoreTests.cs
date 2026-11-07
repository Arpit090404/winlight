using WinLight.Core;

namespace WinLight.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task MissingFileUsesDefaults()
    {
        string path = NewPath();
        WinLightSettings settings = await SettingsStore.LoadAsync(path);
        Assert.Equal(HotkeyBinding.Default, settings.Hotkey);
        Assert.True(settings.StartAtLogin);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Version\":99,\"Hotkey\":{\"Modifiers\":1,\"KeyCode\":32},\"StartAtLogin\":false}")]
    public async Task CorruptOrNewerVersionUsesDefaults(string contents)
    {
        string path = NewPath();
        try
        {
            await File.WriteAllTextAsync(path, contents);
            WinLightSettings settings = await SettingsStore.LoadAsync(path);
            Assert.Equal(HotkeyBinding.Default, settings.Hotkey);
            Assert.True(settings.StartAtLogin);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LegacyUsageSurvivesSettingsMigrationAndConcurrentWrites()
    {
        string path = NewPath();
        try
        {
            await File.WriteAllTextAsync(path,
                "{\"app\":{\"Count\":3,\"LastLaunch\":\"2026-11-06T00:00:00+00:00\"}}");
            var settings = new WinLightSettings { StartAtLogin = false,
                Hotkey = new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4B) };
            await SettingsStore.SaveAsync(path, settings);
            Dictionary<string, UsageRecord> usage = await UsageStore.LoadAsync(path);
            Assert.Equal(3, usage["app"].Count);
            usage["app"] = usage["app"] with { Count = 4 };
            await UsageStore.SaveAsync(path, usage);
            Assert.Equal(settings.Hotkey, (await SettingsStore.LoadAsync(path)).Hotkey);
            Assert.False((await SettingsStore.LoadAsync(path)).StartAtLogin);
            Assert.Equal(4, (await UsageStore.LoadAsync(path))["app"].Count);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void HotkeyRequiresModifierAndRejectsReservedCombinations()
    {
        Assert.False(new HotkeyBinding(HotkeyModifiers.None, 0x41).IsValid(out _));
        Assert.False(new HotkeyBinding(HotkeyModifiers.Windows, 0x4C).IsValid(out _));
        Assert.False(new HotkeyBinding(HotkeyModifiers.Windows | HotkeyModifiers.Control, 0x4B).IsValid(out _));
        Assert.False(new HotkeyBinding(HotkeyModifiers.Alt, 0x09).IsValid(out _));
        Assert.False(new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x2E).IsValid(out _));
        Assert.True(HotkeyBinding.Default.IsValid(out _));
        Assert.True(new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4B).IsValid(out _));
    }

    private static string NewPath() => Path.Combine(Path.GetTempPath(), $"WinLight.Settings.{Guid.NewGuid():N}.json");
}
