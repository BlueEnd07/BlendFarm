using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Windows
{
    /// <summary>Minimal "type a name" dialog (built in code). Returns null when cancelled.</summary>
    public class TextPromptWindow : Window
    {
        private readonly TextBox _input;
        private string _result;

        private TextPromptWindow(string title, string prompt, string initial)
        {
            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (IBrush)Application.Current.FindResource("BlendFarmWindowBrush");

            _input = new TextBox { Text = initial ?? "", Margin = new Thickness(0, 8, 0, 14) };
            var ok = new Button { Content = "Save", Padding = new Thickness(18, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = (IBrush)Application.Current.FindResource("BlendFarmAccentBrush") };
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(18, 6) };
            ok.Click += (_, __) => Accept();
            cancel.Click += (_, __) => Close();

            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap },
                    _input,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } }
                }
            };

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; Accept(); }
                else if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            };
            Opened += (_, __) =>
            {
                _input.Focus();
                _input.SelectAll();
            };
        }

        private void Accept()
        {
            _result = _input.Text?.Trim();
            Close();
        }

        public static async Task<string> Show(Window owner, string title, string prompt, string initial)
        {
            var window = new TextPromptWindow(title, prompt, initial);
            await window.ShowDialog(owner);
            return string.IsNullOrWhiteSpace(window._result) ? null : window._result;
        }
    }
}
