using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LogicReinc.BlendFarm.Windows
{
    /// <summary>Full-window highlight shown while files are dragged over a window.</summary>
    public static class DropOverlay
    {
        public static Border Create(string text)
        {
            return new Border
            {
                IsVisible = false,
                IsHitTestVisible = false,
                ZIndex = 1000,
                Margin = new Thickness(8),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(3),
                BorderBrush = new SolidColorBrush(Color.Parse("#5E87BA")),
                Background = new SolidColorBrush(Color.Parse("#CC1E2F45")),
                Child = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = "⬇", FontSize = 44, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.White },
                        new TextBlock { Text = text, FontSize = 22, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.White }
                    }
                }
            };
        }
    }
}
