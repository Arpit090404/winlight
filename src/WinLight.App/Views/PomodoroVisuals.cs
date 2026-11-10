using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinLight.App.ViewModels;
using WinLight.Core;

namespace WinLight.App;

internal static class PomodoroVisuals
{
    public const int ControlsHeight = 292;
    public const int FloatingWidth = 164;
    public const int FloatingHeight = 80;
    public static FrameworkElement? FloatingDragRegion { get; private set; }

    public static UIElement CreateControls(PomodoroViewModel vm,
        Action<PomodoroSession> select, Action startPause, Action reset, Action skip,
        Action<PomodoroDurations> configure, Action<bool> configureAutoStart)
    {
        ResourceDictionary resources = Resources();
        var content = new StackPanel { Spacing = 8, Padding = new Thickness(16, 8, 16, 12) };
        content.Resources.MergedDictionaries.Add(resources);
        content.Children.Add(new TextBlock
        {
            Text = "Focus",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });

        var hero = new Grid { MinHeight = 72 };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var identity = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var session = new TextBlock { Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
        var status = new TextBlock { Style = (Style)resources["WinLightTimerCaptionStyle"] };
        identity.Children.Add(session);
        identity.Children.Add(status);
        hero.Children.Add(identity);
        var time = new TextBlock
        {
            Style = (Style)resources["WinLightTimerTimeStyle"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(time, 1);
        hero.Children.Add(time);
        content.Children.Add(hero);

        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 4 };
        AutomationProperties.SetName(progress, "Focus session progress");
        content.Children.Add(progress);

        var actionRow = new Grid { ColumnSpacing = 16 };
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var start = IconButton("\uE768", "Start timer", startPause);
        start.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        actions.Children.Add(start);
        actions.Children.Add(IconButton("\uE777", "Reset timer", reset));
        actions.Children.Add(IconButton("\uE76C", "Skip session", skip));
        actionRow.Children.Add(actions);
        var sessions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Button focusSession = SessionButton("Focus", PomodoroSession.Focus);
        Button shortSession = SessionButton("Short break", PomodoroSession.ShortBreak);
        Button longSession = SessionButton("Long break", PomodoroSession.LongBreak);
        sessions.Children.Add(focusSession);
        sessions.Children.Add(shortSession);
        sessions.Children.Add(longSession);
        Grid.SetColumn(sessions, 1);
        actionRow.Children.Add(sessions);
        content.Children.Add(actionRow);

        var details = new Grid { MinHeight = 22 };
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var recommended = new TextBlock { Style = (Style)resources["WinLightTimerCaptionStyle"] };
        var count = new TextBlock { Style = (Style)resources["WinLightTimerCaptionStyle"] };
        details.Children.Add(recommended);
        Grid.SetColumn(count, 1);
        details.Children.Add(count);
        content.Children.Add(details);

        var settingsRow = new Grid { ColumnSpacing = 12 };
        settingsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        settingsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        settingsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        settingsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
        NumberBox focus = DurationBox("Focus min", vm.FocusMinutes);
        NumberBox shortBreak = DurationBox("Short break min", vm.ShortBreakMinutes);
        NumberBox longBreak = DurationBox("Long break min", vm.LongBreakMinutes);
        settingsRow.Children.Add(focus);
        Grid.SetColumn(shortBreak, 1);
        settingsRow.Children.Add(shortBreak);
        Grid.SetColumn(longBreak, 2);
        settingsRow.Children.Add(longBreak);
        var autoStart = new CheckBox
        {
            Content = new TextBlock { Text = "Auto start breaks and focus", TextWrapping = TextWrapping.Wrap },
            IsChecked = vm.AutoStartNextSession,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        AutomationProperties.SetName(autoStart, "Auto start breaks and focus sessions");
        Grid.SetColumn(autoStart, 3);
        settingsRow.Children.Add(autoStart);
        content.Children.Add(settingsRow);

        var panel = new Grid { Height = ControlsHeight };
        panel.Children.Add(new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        var indicator = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
            "Width='3' HorizontalAlignment='Left' Margin='0,8,0,8' " +
            "CornerRadius='2' Background='{ThemeResource AccentFillColorDefaultBrush}' />");
        panel.Children.Add(indicator);
        AutomationProperties.SetName(panel, "Focus timer result");

        bool updating = false;
        Button SessionButton(string title, PomodoroSession sessionType)
        {
            var button = new Button { Content = title, MinHeight = 36, UseSystemFocusVisuals = true };
            AutomationProperties.SetName(button, $"Select {title} session");
            button.Click += (_, _) => select(sessionType);
            return button;
        }
        void ApplyDurations(NumberBox _, NumberBoxValueChangedEventArgs __)
        {
            if (updating || !double.IsFinite(focus.Value) ||
                !double.IsFinite(shortBreak.Value) || !double.IsFinite(longBreak.Value)) return;
            configure(new PomodoroDurations((int)focus.Value, (int)shortBreak.Value,
                (int)longBreak.Value));
        }
        focus.ValueChanged += ApplyDurations;
        shortBreak.ValueChanged += ApplyDurations;
        longBreak.ValueChanged += ApplyDurations;
        autoStart.Checked += (_, _) => { if (!updating) configureAutoStart(true); };
        autoStart.Unchecked += (_, _) => { if (!updating) configureAutoStart(false); };

        void Refresh()
        {
            updating = true;
            session.Text = vm.SessionLabel;
            status.Text = vm.StatusLabel;
            time.Text = vm.RemainingText;
            AutomationProperties.SetName(time, $"{vm.SessionLabel}, {vm.RemainingText} remaining");
            progress.Value = vm.Progress;
            if (start.Content is FontIcon startGlyph)
                startGlyph.Glyph = vm.IsRunning ? "\uE769" : "\uE768";
            AutomationProperties.SetName(start, vm.StartPauseLabel + " timer");
            ToolTipService.SetToolTip(start, vm.StartPauseLabel);
            recommended.Text = vm.RecommendedLabel;
            count.Text = $"Focus sessions: {vm.CompletedFocusSessions}";
            Style accent = (Style)Application.Current.Resources["AccentButtonStyle"];
            SetSessionStyle(focusSession, vm.SessionLabel == "Focus", accent);
            SetSessionStyle(shortSession, vm.SessionLabel == "Short break", accent);
            SetSessionStyle(longSession, vm.SessionLabel == "Long break", accent);
            focusSession.IsEnabled = !vm.IsRunning;
            shortSession.IsEnabled = !vm.IsRunning;
            longSession.IsEnabled = !vm.IsRunning;
            if (focus.Value != vm.FocusMinutes) focus.Value = vm.FocusMinutes;
            if (shortBreak.Value != vm.ShortBreakMinutes) shortBreak.Value = vm.ShortBreakMinutes;
            if (longBreak.Value != vm.LongBreakMinutes) longBreak.Value = vm.LongBreakMinutes;
            if (autoStart.IsChecked != vm.AutoStartNextSession)
                autoStart.IsChecked = vm.AutoStartNextSession;
            indicator.Visibility = vm.IsKeyboardSelected ? Visibility.Visible : Visibility.Collapsed;
            updating = false;
        }
        vm.PropertyChanged += (_, _) => Refresh();
        Refresh();
        return panel;
    }

    private static void SetSessionStyle(Button button, bool selected, Style accent)
    {
        Style? wanted = selected ? accent : null;
        if (!ReferenceEquals(button.Style, wanted)) button.Style = wanted;
    }

    public static UIElement CreateFloating(PomodoroViewModel vm, Action open, Action dismiss)
    {
        ResourceDictionary resources = Resources();
        var root = new Grid { Padding = new Thickness(8, 2, 8, 6) };
        root.Resources.MergedDictionaries.Add(resources);
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var drag = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        var glyph = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        var label = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        identity.Children.Add(glyph);
        identity.Children.Add(label);
        drag.Children.Add(identity);
        header.Children.Add(drag);
        FloatingDragRegion = drag;

        var close = new Button
        {
            Content = new FontIcon { Glyph = "\uE8BB", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 10 },
            Width = 22, Height = 22, Padding = new Thickness(0),
            Background = (Brush)resources["WinLightTimerTransparentBrush"],
            BorderThickness = new Thickness(0), UseSystemFocusVisuals = true
        };
        close.Click += (_, _) => dismiss();
        AutomationProperties.SetName(close, "Dismiss floating timer");
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        root.Children.Add(header);

        var openButton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(2, 0, 2, 0), MinHeight = 30,
            Background = (Brush)resources["WinLightTimerTransparentBrush"],
            BorderThickness = new Thickness(0), UseSystemFocusVisuals = true
        };
        var time = new TextBlock { Style = (Style)resources["WinLightTimerFloatTimeStyle"] };
        openButton.Content = time;
        openButton.Click += (_, _) => open();
        Grid.SetRow(openButton, 1);
        root.Children.Add(openButton);

        var progress = new ProgressBar
        {
            Minimum = 0, Maximum = 100, Height = 3,
            Margin = new Thickness(2, 6, 2, 0)
        };
        AutomationProperties.SetName(progress, "Session progress");
        Grid.SetRow(progress, 2);
        root.Children.Add(progress);

        void Refresh()
        {
            bool completed = vm.StatusLabel == "Completed";
            label.Text = completed ? vm.RecommendedLabel : vm.SessionLabel;
            glyph.Glyph = vm.SessionLabel switch
            {
                "Short break" => "\uE7F1",
                "Long break" => "\uE787",
                _ => "\uE823"
            };
            time.Text = vm.RemainingText;
            progress.Value = vm.Progress;
            AutomationProperties.SetName(openButton, completed
                ? $"{vm.SessionLabel} completed. {vm.RecommendedLabel}. Open timer controls"
                : $"Open {vm.SessionLabel} timer controls, {vm.RemainingText} remaining");
        }
        vm.PropertyChanged += (_, _) => Refresh();
        Refresh();
        return root;
    }

    private static ResourceDictionary Resources() => new()
    {
        Source = new Uri("ms-appx:///Styles/PomodoroResources.xaml")
    };

    private static NumberBox DurationBox(string label, int minutes)
    {
        var box = new NumberBox
        {
            Header = label, Value = minutes, Minimum = 1, Maximum = 180,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(box, label);
        return box;
    }

    private static Button IconButton(string glyph, string name, Action action)
    {
        var button = new Button
        {
            Content = new FontIcon
            {
                Glyph = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 16
            },
            Width = 36, Height = 36,
            MinWidth = 36, MinHeight = 36,
            Padding = new Thickness(0),
            UseSystemFocusVisuals = true
        };
        button.Click += (_, _) => action();
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        return button;
    }
}
