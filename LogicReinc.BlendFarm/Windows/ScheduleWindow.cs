using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LogicReinc.BlendFarm.Client;
using System;

namespace LogicReinc.BlendFarm.Windows
{
    public class ScheduleWindow : Window
    {
        public ScheduleWindow()
        {
            Title = "Start queue at...";
            Width = 420;
            Height = 245;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = ThemeBrushes.Window;
            var day = new ComboBox { Items = new[] { "Today", "Tomorrow" }, SelectedIndex = 0 };
            var time = new TextBox { Text = DateTime.Now.AddMinutes(5).ToString("HH:mm"), Watermark = "HH:mm" };
            var error = new TextBlock { Foreground = ThemeBrushes.Error, TextWrapping = TextWrapping.Wrap };
            var start = new Button { Content = "Schedule", HorizontalAlignment = HorizontalAlignment.Right };
            start.Click += (_, __) =>
            {
                try
                {
                    DateTimeOffset selected = QueueRunState.ParseLocalStart(DateTime.Today, day.SelectedIndex == 1, time.Text);
                    if (selected <= DateTimeOffset.Now) throw new FormatException("Choose a future time (or select Tomorrow).");
                    Close(selected);
                }
                catch (FormatException ex) { error.Text = ex.Message; }
            };
            Content = new StackPanel
            {
                Margin = new Thickness(18), Spacing = 9,
                Children = { new TextBlock { Text = "Local time (HH:mm). The app must remain open.", TextWrapping = TextWrapping.Wrap }, day, time, error, start }
            };
        }
    }
}
