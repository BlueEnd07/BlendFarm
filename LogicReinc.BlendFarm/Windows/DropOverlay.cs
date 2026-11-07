using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LogicReinc.BlendFarm.Windows
{
    /// <summary>Full-window highlight shown while files are dragged over a window.</summary>
    public static class DropOverlay
    {
        //The accent colour at 80% opacity, so the window stays visible underneath
        private static IBrush TranslucentAccent()
        {
            IBrush accent = Statics.ThemeBrush("BlendFarmAccentBrush", "#3E5F8A");
            return new SolidColorBrush(accent is ISolidColorBrush solid ? solid.Color : Color.Parse("#3E5F8A"), 0.8);
        }

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
                BorderBrush = Statics.ThemeBrush("BlendFarmAccentBorderBrush", "#5E87BA"),
                Background = TranslucentAccent(),
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
