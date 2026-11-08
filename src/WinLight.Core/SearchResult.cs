namespace WinLight.Core;

public sealed record SearchResult(
    string Title,
    string? Subtitle,
    string? IconRef,
    double Score,
    string ProviderId,
    ResultAction Action);

public abstract record ResultAction
{
    private ResultAction() { }

    public sealed record LaunchApp(AppItem Item, bool RunAsAdmin = false) : ResultAction
    {
        public string Target => Item.LaunchPath;
        public string? AppUserModelId => Item.AppUserModelId;
    }

    public sealed record CopyText(string Text) : ResultAction;
}
