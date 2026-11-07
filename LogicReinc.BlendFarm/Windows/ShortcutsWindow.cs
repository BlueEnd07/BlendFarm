using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LogicReinc.BlendFarm.Objects;

namespace LogicReinc.BlendFarm.Windows
{
    /// <summary>Small help window listing the keyboard shortcuts (built in code, no XAML).</summary>
    public class ShortcutsWindow : Window
    {
        private static ShortcutsWindow _open;

        public static void ShowFor(Window owner)
        {
            if (_open != null)
            {
                _open.Activate();
                return;
            }
            _open = new ShortcutsWindow();
            _open.Closed += (_, __) => _open = null;
            _open.Show(owner);
        }

        public ShortcutsWindow()
        {
            Title = "Keyboard shortcuts";
            Width = 440;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (IBrush)Application.Current.FindResource("BlendFarmWindowBrush");

            var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*") };
            int row = 0;
            foreach (ShortcutInfo shortcut in ShortcutCatalog.All)
            {
                rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var key = new Border
                {
                    Margin = new Thickness(0, 0, 14, 8),
                    Padding = new Thickness(8, 3),
                    CornerRadius = new CornerRadius(4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = (IBrush)Application.Current.FindResource("BlendFarmRaisedBrush"),
                    BorderBrush = (IBrush)Application.Current.FindResource("BlendFarmSoftBorderBrush"),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = shortcut.Keys, FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontWeight = FontWeight.Bold }
                };
                var description = new TextBlock { Text = shortcut.Description, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
                Grid.SetRow(key, row);
                Grid.SetRow(description, row);
                Grid.SetColumn(description, 1);
                rows.Children.Add(key);
                rows.Children.Add(description);
                row++;
            }

            var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(18, 6), Margin = new Thickness(0, 6, 0, 0) };
            close.Click += (_, __) => Close();

            Content = new StackPanel
            {
                Margin = new Thickness(22),
                Children =
                {
                    new TextBlock { Text = "Keyboard shortcuts", FontSize = 20, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 14) },
                    rows,
                    new TextBlock
                    {
                        Text = "You can also drag a .blend file onto the window to open it. Esc only cancels while a render is running.",
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 8),
                        Foreground = (IBrush)Application.Current.FindResource("BlendFarmMutedTextBrush")
                    },
                    close
                }
            };

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape || e.Key == Key.F1)
                {
                    e.Handled = true;
                    Close();
                }
            };
        }
    }
}
