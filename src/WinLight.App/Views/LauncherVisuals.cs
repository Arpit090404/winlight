using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WinLight.App.ViewModels;
using Windows.UI.ViewManagement;

namespace WinLight.App;

public sealed partial class App
{
    private Storyboard? _resultsFade;

    partial void ConfigureVisuals(StackPanel root, TextBox input, ListView results,
        TextBlock error, LauncherViewModel viewModel)
    {
        var resources = new ResourceDictionary
        {
            Source = new Uri("ms-appx:///Styles/LauncherResources.xaml")
        };
        root.Resources.MergedDictionaries.Add(resources);

        input.Style = (Style)resources["WinLightSearchBoxStyle"];
        Brush transparent = (Brush)resources["WinLightTransparentBrush"];
        foreach (string key in new[]
        {
            "TextControlBackground", "TextControlBackgroundPointerOver",
            "TextControlBackgroundFocused", "TextControlBackgroundDisabled",
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver",
            "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled"
        })
            input.Resources[key] = transparent;
        input.PlaceholderText = "Search apps";
        input.VerticalAlignment = VerticalAlignment.Center;
        input.HorizontalAlignment = HorizontalAlignment.Stretch;
        input.Margin = new Thickness(52, 0, 16, 0);
        AutomationProperties.SetName(input, "Search apps");

        var searchBar = new Grid { Height = 56 };
        root.Children.Clear();
        var glyph = new FontIcon
        {
            Glyph = "\uE721",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        searchBar.Children.Add(glyph);
        searchBar.Children.Add(input);

        results.DisplayMemberPath = string.Empty;
        results.ItemTemplate = (DataTemplate)resources["WinLightResultTemplate"];
        results.ItemContainerStyle = (Style)resources["WinLightResultContainerStyle"];
        results.Padding = new Thickness(0);
        results.BorderThickness = new Thickness(0);
        results.SelectionMode = ListViewSelectionMode.Single;
        AutomationProperties.SetName(results, "App search results");
        results.SelectionChanged += (_, _) =>
        {
            UpdateSelectionPills(results);
            results.DispatcherQueue.TryEnqueue(() => UpdateSelectionPills(results));
        };
        results.LayoutUpdated += (_, _) => UpdateSelectionPills(results);

        var divider = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
            "Height='1' Margin='0,0,0,-1' Visibility='Collapsed' " +
            "Background='{ThemeResource DividerStrokeColorDefaultBrush}' />");
        root.Children.Add(searchBar);
        root.Children.Add(divider);
        root.Children.Add(results);
        root.Children.Add(error);

        var uiSettings = new UISettings();
        viewModel.PropertyChanged += OnResultsVisibilityChanged;

        void OnResultsVisibilityChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(LauncherViewModel.IsResultsVisible)) return;
            divider.Visibility = viewModel.IsResultsVisible ? Visibility.Visible : Visibility.Collapsed;
            _resultsFade?.Stop();
            results.Opacity = 1;
            if (!viewModel.IsResultsVisible || !uiSettings.AnimationsEnabled) return;

            results.Opacity = 0;
            results.DispatcherQueue.TryEnqueue(() =>
            {
                if (!viewModel.IsResultsVisible) return;
                var fade = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = new Duration(TimeSpan.FromMilliseconds(120))
                };
                _resultsFade = new Storyboard();
                Storyboard.SetTarget(fade, results);
                Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));
                _resultsFade.Children.Add(fade);
                _resultsFade.Begin();
            });
        }
    }

    private static void UpdateSelectionPills(ListView results)
    {
        for (int index = 0; index < results.Items.Count; index++)
        {
            if (results.ContainerFromIndex(index) is not ListViewItem container) continue;
            Border? pill = FindSelectionPill(container);
            if (pill is null) continue;
            Visibility wanted = index == results.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
            if (pill.Visibility != wanted) pill.Visibility = wanted;
        }
    }

    private static Border? FindSelectionPill(DependencyObject parent)
    {
        if (parent is Border border && Equals(border.Tag, "WinLightSelectionPill")) return border;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            Border? found = FindSelectionPill(VisualTreeHelper.GetChild(parent, index));
            if (found is not null) return found;
        }
        return null;
    }
}
