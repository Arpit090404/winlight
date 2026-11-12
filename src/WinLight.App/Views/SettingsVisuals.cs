using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace WinLight.App;

public sealed partial class App
{
    private ListView? _settingsCategories;
    private Storyboard? _settingsFade;

    partial void ConfigureSettingsVisuals(StackPanel root, TextBox hotkeyCapture,
        ToggleSwitch startupToggle, Button rescanButton, TextBlock message,
        Action returnToSearch)
    {
        var resources = new ResourceDictionary
        {
            Source = new Uri("ms-appx:///Styles/SettingsResources.xaml")
        };
        root.Resources.MergedDictionaries.Add(resources);
        root.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) =>
        {
            _settingsFade?.Stop();
            root.Opacity = 1;
            if (root.Visibility != Visibility.Visible || _motionSettings?.AnimationsEnabled != true) return;
            _settingsFade = Fade(root, 0, 1, 140);
            _settingsFade.Begin();
        });
        root.Padding = new Thickness(0);
        root.Children.Clear();

        hotkeyCapture.Header = "Keyboard shortcut";
        hotkeyCapture.Style = (Style)resources["WinLightSettingsHotkeyStyle"];
        AutomationProperties.SetName(hotkeyCapture, "Keyboard shortcut. Press a new combination to change it.");
        AutomationProperties.SetHelpText(hotkeyCapture,
            "The current shortcut is shown. Press a modifier and another key to replace it.");

        var shortcutGroup = new StackPanel { Spacing = 4 };
        shortcutGroup.Children.Add(hotkeyCapture);
        shortcutGroup.Children.Add(new TextBlock
        {
            Text = "Press a modifier and key to change the shortcut.",
            Style = (Style)resources["WinLightSettingsHintStyle"]
        });

        startupToggle.Header = "Start at login";
        AutomationProperties.SetName(startupToggle, "Start WinLight at login");

        var general = new StackPanel { Spacing = 16 };
        general.Children.Add(new TextBlock
        {
            Text = "General",
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"]
        });
        general.Children.Add(shortcutGroup);
        general.Children.Add(HorizontalDivider());
        general.Children.Add(startupToggle);

        rescanButton.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(rescanButton, "Rescan installed apps");
        var apps = new StackPanel { Spacing = 16, Visibility = Visibility.Collapsed };
        apps.Children.Add(new TextBlock
        {
            Text = "Apps",
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"]
        });
        apps.Children.Add(rescanButton);

        message.Style = (Style)resources["WinLightSettingsStatusStyle"];
        AutomationProperties.SetName(message, "Settings status");

        var sidebar = new StackPanel { Spacing = 12, Padding = new Thickness(12) };
        var back = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon
                    {
                        Glyph = "\uE72B",
                        FontFamily = new FontFamily("Segoe Fluent Icons"),
                        FontSize = 16
                    },
                    new TextBlock { Text = "Search" }
                }
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = (Brush)resources["WinLightSettingsTransparentBrush"],
            BorderThickness = new Thickness(0),
            UseSystemFocusVisuals = true
        };
        AutomationProperties.SetName(back, "Return to Search");
        back.Click += (_, _) => returnToSearch();
        sidebar.Children.Add(back);
        sidebar.Children.Add(new TextBlock
        {
            Text = "Settings",
            Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });

        _settingsCategories = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemContainerStyle = (Style)resources["WinLightSettingsCategoryStyle"]
        };
        AutomationProperties.SetName(_settingsCategories, "Settings categories");
        var generalItem = new ListViewItem { Content = "General" };
        var appsItem = new ListViewItem { Content = "Apps" };
        AutomationProperties.SetName(generalItem, "General settings");
        AutomationProperties.SetName(appsItem, "Apps settings");
        _settingsCategories.Items.Add(generalItem);
        _settingsCategories.Items.Add(appsItem);
        _settingsCategories.SelectionChanged += (_, _) =>
        {
            bool showApps = _settingsCategories.SelectedIndex == 1;
            general.Visibility = showApps ? Visibility.Collapsed : Visibility.Visible;
            apps.Visibility = showApps ? Visibility.Visible : Visibility.Collapsed;
        };
        _settingsCategories.SelectedIndex = 0;
        sidebar.Children.Add(_settingsCategories);

        var content = new StackPanel { Spacing = 16, Padding = new Thickness(20, 16, 20, 16) };
        content.Children.Add(general);
        content.Children.Add(apps);
        content.Children.Add(message);
        var layout = new Grid { Height = 360 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(168) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(sidebar);
        var divider = VerticalDivider();
        Grid.SetColumn(divider, 1);
        layout.Children.Add(divider);
        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetColumn(scroll, 2);
        layout.Children.Add(scroll);
        root.Children.Add(layout);
    }

    private partial void FocusSettingsNavigation() =>
        _settingsCategories?.Focus(FocusState.Programmatic);

    private static Border HorizontalDivider() => (Border)XamlReader.Load(
        "<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
        "Height='1' Background='{ThemeResource DividerStrokeColorDefaultBrush}' />");

    private static Border VerticalDivider() => (Border)XamlReader.Load(
        "<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
        "Width='1' Background='{ThemeResource DividerStrokeColorDefaultBrush}' />");
}
