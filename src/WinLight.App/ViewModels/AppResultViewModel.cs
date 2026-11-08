using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using WinLight.Core;

namespace WinLight.App.ViewModels;

public partial class AppResultViewModel(SearchResult result) : ObservableObject
{
    public SearchResult Result { get; } = result;
    public string Name => Result.Title;
    public string? Subtitle => Result.Subtitle;
    public string AccessibleName => Result.Subtitle is null ? Name : $"{Result.Subtitle} {Name}";
    public bool IsCalculator => Result.ProviderId == "calculator";
    public string? IconRef => Result.IconRef;

    [ObservableProperty]
    public partial BitmapImage? Icon { get; set; }
}
