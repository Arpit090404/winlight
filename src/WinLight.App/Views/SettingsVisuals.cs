using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace WinLight.App;

public sealed partial class App
{
    partial void ConfigureSettingsVisuals(StackPanel root, TextBox hotkeyCapture,
        ToggleSwitch startupToggle, Button rescanButton, TextBlock message)
    {
        var resources = new ResourceDictionary
        {
            Source = new Uri("ms-appx:///Styles/SettingsResources.xaml")
        };
        root.Resources.MergedDictionaries.Add(resources);
        root.Padding = new Thickness(20, 12, 20, 12);
        root.Children.Clear();
        var content = new StackPanel { Spacing = 12 };

        hotkeyCapture.Header = "Keyboard shortcut";
        hotkeyCapture.Style = (Style)resources["WinLightSettingsHotkeyStyle"];
        hotkeyCapture.TabIndex = 0;
        AutomationProperties.SetName(hotkeyCapture, "Keyboard shortcut. Press a new combination to change it.");
        AutomationProperties.SetHelpText(hotkeyCapture, "The current shortcut is shown. Press a modifier and another key to replace it.");

        var shortcutGroup = new StackPanel { Spacing = 4 };
        shortcutGroup.Children.Add(hotkeyCapture);
        var hint = new TextBlock
        {
            Text = "Press a modifier and key to change the shortcut.",
            Style = (Style)resources["WinLightSettingsHintStyle"]
        };
        shortcutGroup.Children.Add(hint);
        content.Children.Add(shortcutGroup);

        var divider = (Border)XamlReader.Load(
            "<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
            "Height='1' Background='{ThemeResource DividerStrokeColorDefaultBrush}' />");
        content.Children.Add(divider);

        startupToggle.Header = "Start at login";
        startupToggle.TabIndex = 1;
        AutomationProperties.SetName(startupToggle, "Start WinLight at login");
        content.Children.Add(startupToggle);

        rescanButton.HorizontalAlignment = HorizontalAlignment.Left;
        rescanButton.TabIndex = 2;
        AutomationProperties.SetName(rescanButton, "Rescan installed apps");
        content.Children.Add(rescanButton);

        message.Style = (Style)resources["WinLightSettingsStatusStyle"];
        AutomationProperties.SetName(message, "Settings status");
        content.Children.Add(message);

        root.Children.Add(new ScrollViewer
        {
            Content = content,
            MaxHeight = 280,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });

        root.Loaded += (_, _) =>
        {
            if (_settingsWindow is not null)
            {
                _settingsWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(440, 340));
                _settingsWindow.SystemBackdrop = new DesktopAcrylicBackdrop();
            }
        };
    }
}
