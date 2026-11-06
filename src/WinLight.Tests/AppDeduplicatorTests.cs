using WinLight.Core;

namespace WinLight.Tests;

public sealed class AppDeduplicatorTests
{
    [Fact]
    public void AppsFolderWinsOverClassicShortcutEvenWhenItArrivesLater()
    {
        var shortcut = new AppItem("Android Studio", "studio.lnk", "studio.lnk",
            AppSource.CommonStartMenu, @"C:\Program Files\Android\Android Studio\bin\studio64.exe");
        var appsFolder = new AppItem("Android Studio", "shell:AppsFolder\\studio", "studio",
            AppSource.AppsFolder, @"C:\Program Files\Android\Android Studio\bin\studio64.exe", "studio.aumid");

        Assert.Same(appsFolder, Assert.Single(AppDeduplicator.Deduplicate([shortcut, appsFolder])));
    }

    [Fact]
    public void AppsFolderAumidAndShortcutAumidAreOneApp()
    {
        var shortcut = new AppItem("Control Panel", "control.lnk", "control.lnk",
            AppSource.UserStartMenu, null, "Microsoft.Windows.ControlPanel");
        var appsFolder = new AppItem("Control Panel", "shell:AppsFolder\\Microsoft.Windows.ControlPanel",
            "Microsoft.Windows.ControlPanel", AppSource.AppsFolder, "::{control-panel}", "Microsoft.Windows.ControlPanel");

        Assert.Same(appsFolder, Assert.Single(AppDeduplicator.Deduplicate([shortcut, appsFolder])));
    }

    [Fact]
    public void PerUserAndCommonShortcutsDedupeDespitePathCasing()
    {
        var user = new AppItem("Steam", "user.lnk", "user.lnk",
            AppSource.UserStartMenu, @"C:\Program Files (x86)\Steam\steam.exe");
        var common = new AppItem("Steam", "common.lnk", "common.lnk",
            AppSource.CommonStartMenu, @"c:\program files (x86)\steam\Steam.exe");

        Assert.Single(AppDeduplicator.Deduplicate([common, user]));
    }

    [Fact]
    public void ResolvedTargetCollapsesRootFolderAndStartupShortcuts()
    {
        var root = new AppItem("Ollama", "root.lnk", "root.lnk", AppSource.UserStartMenu,
            @"C:\Users\A\AppData\Local\Programs\Ollama\ollama app.exe");
        var folder = new AppItem("Ollama", "folder.lnk", "folder.lnk", AppSource.UserStartMenu,
            @"C:\Users\A\AppData\Local\Programs\Ollama\ollama app.exe");
        var startup = new AppItem("Ollama", "startup.lnk", "startup.lnk", AppSource.UserStartMenu,
            @"C:\Users\A\AppData\Local\Programs\Ollama\ollama app.exe");

        Assert.Single(AppDeduplicator.Deduplicate([root, folder, startup]));
    }

    [Fact]
    public void SameNameWithDifferentTargetsRemainsTwoApps()
    {
        var first = new AppItem("Notes", "first.lnk", "first.lnk", AppSource.UserStartMenu, @"C:\One\Notes.exe");
        var second = new AppItem("Notes", "second.lnk", "second.lnk", AppSource.CommonStartMenu, @"C:\Two\Notes.exe");

        Assert.Equal(2, AppDeduplicator.Deduplicate([first, second]).Length);
    }

    [Fact]
    public void SameTargetWithDifferentLaunchArgumentsRemainsTwoActions()
    {
        var first = new AppItem("Tool", "first.lnk", "first.lnk", AppSource.UserStartMenu, @"C:\Tool.exe", arguments: "--profile one");
        var second = new AppItem("Tool", "second.lnk", "second.lnk", AppSource.UserStartMenu, @"C:\Tool.exe", arguments: "--profile two");

        Assert.Equal(2, AppDeduplicator.Deduplicate([first, second]).Length);
    }
}
