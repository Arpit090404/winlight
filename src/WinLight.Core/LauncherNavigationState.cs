namespace WinLight.Core;

/// <summary>Keeps the search query while the launcher shows its settings content.</summary>
public sealed class LauncherNavigationState
{
    private string _lastSearchQuery = string.Empty;

    public bool IsSettingsOpen { get; private set; }
    public string SearchQuery { get; private set; } = string.Empty;

    public void RecordSearchQuery(string query)
    {
        if (!IsSettingsOpen) _lastSearchQuery = query;
    }

    public void EnterSettings(string currentQuery, bool fromQuery)
    {
        if (IsSettingsOpen) return;
        SearchQuery = fromQuery ? _lastSearchQuery : currentQuery;
        IsSettingsOpen = true;
    }

    public bool SearchTextChanged(string query)
    {
        if (!IsSettingsOpen) return false;
        IsSettingsOpen = false;
        _lastSearchQuery = query;
        return true;
    }

    public string ReturnToSearch()
    {
        IsSettingsOpen = false;
        _lastSearchQuery = SearchQuery;
        return SearchQuery;
    }
}
