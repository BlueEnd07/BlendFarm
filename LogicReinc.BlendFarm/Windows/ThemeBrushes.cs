using Avalonia;
using Avalonia.Media;

namespace LogicReinc.BlendFarm.Windows
{
    /// <summary>Looks up the brushes from App.axaml / Styles/MainStyle.axaml for windows that are built in code.</summary>
    internal static class ThemeBrushes
    {
        public static IBrush Get(string key, string fallbackColor)
        {
            if (Application.Current != null && ((Avalonia.Controls.IResourceNode)Application.Current).TryGetResource(key, out object value) && value is IBrush brush)
                return brush;
            return new SolidColorBrush(Color.Parse(fallbackColor));
        }

        public static IBrush Window => Get("BlendFarmWindowBrush", "#262626");
        public static IBrush Error => Get("BlendFarmErrorBrush", "#FF5066");
    }
}
