using WinLight.Core;

namespace WinLight.Tests;

public sealed class LauncherSettingsIntegrationTests
{
    [Fact]
    public void GearAndQueryNavigationPreserveSearchAndNewTypingReturnsToSearch()
    {
        var navigation = new LauncherNavigationState();
        navigation.RecordSearchQuery("notepad");
        navigation.EnterSettings("notepad", fromQuery: false);
        Assert.True(navigation.IsSettingsOpen);
        Assert.Equal("notepad", navigation.ReturnToSearch());
        Assert.False(navigation.IsSettingsOpen);

        navigation.RecordSearchQuery("notepad");
        navigation.EnterSettings("settings", fromQuery: true);
        Assert.True(navigation.IsSettingsOpen);
        Assert.Equal("notepad", navigation.ReturnToSearch());

        navigation.EnterSettings("notepad", fromQuery: false);
        Assert.True(navigation.SearchTextChanged("calculator"));
        Assert.False(navigation.IsSettingsOpen);
        navigation.EnterSettings("calculator", fromQuery: false);
        Assert.Equal("calculator", navigation.ReturnToSearch());
    }
}
