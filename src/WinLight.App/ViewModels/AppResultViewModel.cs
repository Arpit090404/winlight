using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using WinLight.Core;

namespace WinLight.App.ViewModels;

public partial class AppResultViewModel(AppItem item) : ObservableObject
{
    public AppItem Item { get; } = item;
    public string Name => Item.Name;

    [ObservableProperty]
    public partial BitmapImage? Icon { get; set; }
}
