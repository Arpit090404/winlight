using CommunityToolkit.Mvvm.ComponentModel;

namespace WinLight.App.ViewModels;

public partial class LauncherViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsResultsVisible { get; set; }

    public void SetQuery(string query) => IsResultsVisible = !string.IsNullOrWhiteSpace(query);
}
