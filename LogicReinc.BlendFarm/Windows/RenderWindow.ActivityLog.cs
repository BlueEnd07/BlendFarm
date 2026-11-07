using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Objects;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Windows
{
    // Activity log tools: level / node / text filter, copy and export. The log itself (ActivityLog) is untouched;
    // the list shows a filtered mirror of it.
    public partial class RenderWindow
    {
        private const string AllNodesLabel = "All nodes";
        private static readonly string[] LogLevelLabels = { "All", "Info", "Warnings", "Errors" };

        private readonly ActivityLogFilter _logFilter = new ActivityLogFilter();
        private ObservableCollection<RenderActivityLogEntry> _visibleLog = new ObservableCollection<RenderActivityLogEntry>();
        //Node titles seen so far → node key (node name). Titles change when a node learns its computer name.
        private readonly Dictionary<string, string> _logNodeByTitle = new Dictionary<string, string>();
        private readonly ObservableCollection<string> _logNodeChoices = new ObservableCollection<string>();
        private ComboBox _logLevelFilter;
        private ComboBox _logNodeFilter;
        private TextBox _logSearch;
        private Button _logCopyButton;
        private bool _updatingLogFilters;
        private DispatcherTimer _copyFlashTimer;

        private void InitializeActivityLogTools()
        {
            _logLevelFilter = this.FindControl<ComboBox>("logLevelFilter");
            _logNodeFilter = this.FindControl<ComboBox>("logNodeFilter");
            _logSearch = this.FindControl<TextBox>("logSearch");
            _logCopyButton = this.FindControl<Button>("logCopyButton");
            if (_activityLogList == null || _logLevelFilter == null || _logNodeFilter == null || _logSearch == null)
                return;

            _logNodeChoices.Add(AllNodesLabel);
            _logLevelFilter.Items = LogLevelLabels;
            _logLevelFilter.SelectedIndex = 0;
            _logNodeFilter.Items = _logNodeChoices;
            _logNodeFilter.SelectedIndex = 0;

            _logLevelFilter.SelectionChanged += (_, __) => OnLogFilterChanged();
            _logNodeFilter.SelectionChanged += (_, __) => OnLogFilterChanged();
            _logSearch.GetObservable(TextBox.TextProperty).Subscribe(_ => OnLogFilterChanged());

            ActivityLog.CollectionChanged += OnActivityLogCollectionChanged;
            Nodes.CollectionChanged += (_, __) => LearnLogNodes();
            LearnLogNodes();
            RebuildVisibleLog();
        }

        private void OnLogFilterChanged()
        {
            if (_updatingLogFilters)
                return;
            _logFilter.Level = _logLevelFilter.SelectedIndex switch
            {
                1 => ActivityLogLevelFilter.Info,
                2 => ActivityLogLevelFilter.Warnings,
                3 => ActivityLogLevelFilter.Errors,
                _ => ActivityLogLevelFilter.All
            };
            string node = _logNodeFilter.SelectedItem as string;
            _logFilter.Node = node == null || node == AllNodesLabel ? null : node;
            _logFilter.Search = _logSearch.Text;
            RebuildVisibleLog();
        }

        private void OnActivityLogCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    LearnLogNodes();
                    foreach (RenderActivityLogEntry entry in e.NewItems)
                        if (_logFilter.Matches(entry, _logNodeByTitle))
                            _visibleLog.Add(entry);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    foreach (RenderActivityLogEntry entry in e.OldItems)
                        _visibleLog.Remove(entry);
                    break;
                default:
                    RebuildVisibleLog();
                    break;
            }
        }

        // Registers the titles of the current nodes so older entries stay attributable after a title change.
        private void LearnLogNodes()
        {
            foreach (RenderNode node in Nodes.ToList())
            {
                if (string.IsNullOrWhiteSpace(node?.Name))
                    continue;
                _logNodeByTitle[node.Name] = node.Name;
                string title = node.NodeTitle;
                if (!string.IsNullOrWhiteSpace(title))
                    _logNodeByTitle[title] = node.Name;
                if (!_logNodeChoices.Contains(node.Name))
                    _logNodeChoices.Add(node.Name);
            }
        }

        private void RebuildVisibleLog()
        {
            if (_activityLogList == null)
                return;
            _visibleLog = new ObservableCollection<RenderActivityLogEntry>(_logFilter.Apply(ActivityLog, _logNodeByTitle));
            _activityLogList.Items = _visibleLog;
            Dispatcher.UIThread.Post(() =>
            {
                if (_visibleLog.Count > 0)
                    _activityLogList.ScrollIntoView(_visibleLog[_visibleLog.Count - 1]);
            }, DispatcherPriority.Background);
        }

        public async void CopyLogClicked(object sender, RoutedEventArgs e)
        {
            string text = ActivityLogFilter.FormatEntries(_visibleLog);
            try
            {
                if (Application.Current?.Clipboard != null)
                    await Application.Current.Clipboard.SetTextAsync(text);
                FlashCopyButton($"Copied {_visibleLog.Count}");
            }
            catch (Exception ex)
            {
                AddActivityLog($"Could not copy the log: {ex.Message}", ActivityLogType.Error);
            }
        }

        private void FlashCopyButton(string text)
        {
            if (_logCopyButton == null)
                return;
            _logCopyButton.Content = text;
            _copyFlashTimer?.Stop();
            _copyFlashTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _copyFlashTimer.Tick += (_, __) =>
            {
                _logCopyButton.Content = "Copy";
                _copyFlashTimer.Stop();
            };
            _copyFlashTimer.Start();
        }

        public async void ExportLogClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                var entries = _visibleLog.ToList();
                var dialog = new SaveFileDialog
                {
                    Title = "Export activity log",
                    InitialFileName = $"BlendFarm-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                    DefaultExtension = "txt",
                    Filters = new List<FileDialogFilter>
                    {
                        new FileDialogFilter { Name = "Text file (.txt)", Extensions = { "txt" } },
                        new FileDialogFilter { Name = "Log file (.log)", Extensions = { "log" } }
                    }
                };
                string path = Statics.SanitizePath(await dialog.ShowAsync(this));
                if (string.IsNullOrEmpty(path))
                    return;
                var sb = new StringBuilder();
                sb.AppendLine($"BlendFarm activity log, exported {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                if (_logFilter.IsActive)
                    sb.AppendLine($"Filter: level={_logFilter.Level}, node={_logFilter.Node ?? "all"}, search=\"{_logFilter.Search}\"");
                sb.AppendLine();
                sb.Append(ActivityLogFilter.FormatEntries(entries));
                await Task.Run(() => File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false)));
                AddActivityLog($"Exported {entries.Count} log entries to {path}", ActivityLogType.Success);
            }
            catch (Exception ex)
            {
                await MessageWindow.Show(this, "Export failed", ex.Message);
            }
        }
    }
}
