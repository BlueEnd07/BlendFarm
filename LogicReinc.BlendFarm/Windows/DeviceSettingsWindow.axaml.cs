using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static LogicReinc.BlendFarm.BlendFarmSettings;

namespace LogicReinc.BlendFarm.Windows
{
    public class DeviceSettingsWindow : Window
    {
        private ComboBox selectRenderType = null;
        private TextBox inputNodeName = null;
        private TextBlock saveStatus = null;
        private string originalNodeName = null;

        public RenderType[] RenderTypes { get; } = (RenderType[])Enum.GetValues(typeof(RenderType));
        public RenderNode Node { get; set; }

        public DeviceSettingsWindow()
        {
            Node = new RenderNode()
            {
                Name = "Some Device Name",
                Activity = "SomeActivity",
                Cores = 16,
                ComputerName = "SomeDesktopName",
                OS = "windows64",
                RenderType = RenderType.OPTIX_GPUONLY,
                Address = "192.168.1.123:15000"
            };
            DataContext = this;
            this.InitializeComponent();
        }
        public DeviceSettingsWindow(RenderNode node)
        {
            Node = node;
            DataContext = this;
            this.InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            selectRenderType = this.Find<ComboBox>("selectRenderType");
            inputNodeName = this.Find<TextBox>("inputNodeName");
            saveStatus = this.Find<TextBlock>("saveStatus");
            originalNodeName = Node.Name;
            selectRenderType.SelectedItem = Node.RenderType;
            Width = 460;
            Height = 520;
            MinHeight = 520;
            MinWidth = 460;
        }


        public async void Save()
        {
            string newName = inputNodeName.Text?.Trim();
            if (string.IsNullOrEmpty(newName))
            {
                saveStatus.Text = "Name is required";
                return;
            }

            BlendFarmSettings.Instance.PastClients ??= new Dictionary<string, HistoryClient>();

            if (newName != originalNodeName && BlendFarmSettings.Instance.PastClients.Any(x => x.Key == newName))
            {
                saveStatus.Text = "Name already exists";
                return;
            }

            string entryKey = null;
            HistoryClient entry = null;
            var namedEntry = BlendFarmSettings.Instance.PastClients.FirstOrDefault(x => x.Key == originalNodeName);
            if (namedEntry.Value != null)
            {
                entryKey = namedEntry.Key;
                entry = namedEntry.Value;
            }
            else
            {
                var addressEntry = BlendFarmSettings.Instance.PastClients.FirstOrDefault(x => x.Value.Address == Node.Address);
                entryKey = addressEntry.Key;
                entry = addressEntry.Value;
            }

            if (entry == null)
                entry = new HistoryClient();

            if (!string.IsNullOrEmpty(entryKey))
                BlendFarmSettings.Instance.PastClients.Remove(entryKey);

            BlendFarmSettings.Instance.RenameNodeFlags(originalNodeName, newName);
            Node.Name = newName;
            Node.RenderType = selectRenderType.SelectedItem is RenderType type ? type : RenderType.OPTIX_GPUONLY;

            entry.Name = Node.Name;
            entry.Address = Node.Address;
            entry.RenderType = Node.RenderType;
            entry.Pass = Node.Pass;
            entry.Performance = Node.Performance;
            BlendFarmSettings.Instance.PastClients[Node.Name] = entry;
            BlendFarmSettings.Instance.Save();

            originalNodeName = Node.Name;
            saveStatus.Text = "Saved";
            await Task.Delay(650);
            Close();
        }

        public static async Task Show(Window owner, RenderNode node)
        {
            var window = new DeviceSettingsWindow(node);

            window.Position = new PixelPoint((int)(owner.Position.X + ((owner.Width / 2) - window.Width / 2)), (int)(owner.Position.Y + ((owner.Height / 2) - window.Height / 2)));

            await window.ShowDialog(owner);
        }
    }
}
