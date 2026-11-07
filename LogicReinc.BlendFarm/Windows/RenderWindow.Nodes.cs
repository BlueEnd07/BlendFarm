using Avalonia.Threading;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Objects;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Windows
{
    public partial class RenderWindow
    {
        public bool AutoReconnectNodes
        {
            get => BlendFarmSettings.Instance.AutoReconnectNodes;
            set
            {
                BlendFarmSettings.Instance.AutoReconnectNodes = value;
                foreach (var node in Nodes.ToList())
                    node.ConfigureReconnect(value, () => Version.Name);
                BlendFarmSettings.Instance.TrySave();
            }
        }

        private void AttachNodeManagement(RenderNode node)
        {
            if (BlendFarmSettings.Instance.PastClients.TryGetValue(node.Name, out var saved))
                node.IsEnabled = saved.IsEnabled;
            node.ConfigureReconnect(AutoReconnectNodes, () => Version.Name);
            node.PropertyChanged += NodeManagementChanged;
            node.ConnectionFailed += NodeConnectionFailed;
        }

        private void DetachNodeManagement(RenderNode node)
        {
            node.ConfigureReconnect(false, null);
            node.PropertyChanged -= NodeManagementChanged;
            node.ConnectionFailed -= NodeConnectionFailed;
        }

        private void NodeConnectionFailed(RenderNode node, string message)
            => Dispatcher.UIThread.Post(() => AddActivityLog($"{node.NodeTitle}: {message}", ActivityLogType.Error));

        private void NodeManagementChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(RenderNode.IsEnabled) || !(sender is RenderNode node)) return;
            Dispatcher.UIThread.Post(() =>
            {
                var settings = BlendFarmSettings.Instance;
                if (!settings.PastClients.TryGetValue(node.Name, out var saved))
                {
                    saved = new BlendFarmSettings.HistoryClient
                    {
                        Name = node.Name, Address = node.Address, Pass = node.Pass,
                        RenderType = node.RenderType, Performance = node.Performance,
                        PerformanceScorePP = node.PerformanceScorePP
                    };
                    settings.PastClients[node.Name] = saved;
                }
                saved.IsEnabled = node.IsEnabled;
                settings.TrySave();
                AddActivityLog($"{node.NodeTitle} {(node.IsEnabled ? "enabled" : "disabled")} for rendering");
            });
        }

        private List<RenderNode> SelectedNodes() => Nodes.Where(node => node.IsSelected).ToList();
        public void SelectAllNodes() { foreach (var node in Nodes) node.IsSelected = true; }
        public void SelectNoNodes() { foreach (var node in Nodes) node.IsSelected = false; }
        public void EnableSelectedNodes() { foreach (var node in SelectedNodes()) node.IsEnabled = true; }
        public void DisableSelectedNodes() { foreach (var node in SelectedNodes()) node.IsEnabled = false; }

        public async void ConnectManagedNode(RenderNode node)
        {
            try { await node.ConnectAndPrepare(Version.Name); }
            catch (Exception error) { node.UpdateException(NodeConnectionPolicy.DescribeFailure(error)); }
        }

        public async void ConnectSelectedNodes()
        {
            var selected = SelectedNodes();
            if (selected.Count == 0) { await ShowNoSelection(); return; }
            await Task.WhenAll(selected.Select(async node =>
            {
                try { await node.ConnectAndPrepare(Version.Name); }
                catch (Exception error) { node.UpdateException(NodeConnectionPolicy.DescribeFailure(error)); }
            }));
        }

        private Task SyncManagedNodes(OpenBlenderProject project, IEnumerable<RenderNode> nodes)
            => project.UseNetworkedPath
                ? Manager.Sync(project.BlendFile, project.NetworkPathWindows, project.NetworkPathLinux, project.NetworkPathMacOS, nodes)
                : Manager.Sync(project.BlendFile, UseSyncCompression, nodes);

        public async void SyncSelectedNodes()
        {
            var selected = SelectedNodes().Where(node => node.IsEnabled && node.Connected).ToList();
            if (selected.Count == 0) { await ShowNoSelection(); return; }
            if (CurrentProject == null || Manager == null) return;
            try { await SyncManagedNodes(CurrentProject, selected); }
            catch (Exception error) { await MessageWindow.Show(this, "Sync failed", error.Message); }
        }

        public async void BenchmarkSelectedNodes()
        {
            var selected = SelectedNodes().Where(node => node.IsEnabled && node.Connected).ToList();
            if (selected.Count == 0) { await ShowNoSelection(); return; }
            await BenchmarkNodesInternal(selected);
        }

        private Task ShowNoSelection() => MessageWindow.Show(this, "Select nodes", "Select node checkboxes first. Sync and benchmark require enabled, connected nodes.");

        public async void RemoveSelectedNodes()
        {
            var selected = SelectedNodes();
            if (selected.Count == 0) { await ShowNoSelection(); return; }
            if (!await YesNoWindow.Show(this, "Remove selected nodes", $"Disconnect and remove {selected.Count} selected node(s)?")) return;
            foreach (var node in selected) DeleteNode(node);
        }
    }
}
