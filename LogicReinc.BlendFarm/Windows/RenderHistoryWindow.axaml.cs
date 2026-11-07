using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LogicReinc.BlendFarm.Objects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LogicReinc.BlendFarm.Windows
{
    /// <summary>
    /// What one history card shows, with the file checks done up front so missing files degrade gracefully.
    /// </summary>
    public class RenderHistoryItem
    {
        public RenderHistoryEntry Entry { get; }
        public Bitmap Thumbnail { get; }
        public string ImageFile { get; }
        public string FolderPath { get; }

        public bool HasThumbnail => Thumbnail != null;
        public bool IsAnimation => Entry.IsAnimation;
        public bool CanOpenImage => ImageFile != null;
        public bool CanOpenFolder => FolderPath != null;
        public bool HasStatus => !string.IsNullOrEmpty(StatusText);
        public string StatusText { get; }

        public string Title { get; }
        public string DateText => Entry.Date.ToString("g");
        public string DetailText { get; }
        public string PathText { get; }
        public string PathTip { get; }
        public string FrameBadge => $"{Entry.FrameCount} frames ({Entry.FrameStart}-{Entry.FrameEnd})";

        public RenderHistoryItem(RenderHistoryEntry entry, RenderHistoryStore store)
        {
            Entry = entry;

            string thumb = store.ResolveAsset(entry.ThumbnailFile);
            if (thumb != null && File.Exists(thumb))
            {
                try { Thumbnail = new Bitmap(thumb); }
                catch { Thumbnail = null; }
            }

            string output = entry.OutputPath;
            string preview = store.ResolveAsset(entry.PreviewFile);
            bool outputExists = !string.IsNullOrEmpty(output) && File.Exists(output);
            bool previewExists = !string.IsNullOrEmpty(preview) && File.Exists(preview);
            ImageFile = outputExists ? output : previewExists ? preview : null;

            string folder = !string.IsNullOrEmpty(entry.OutputDirectory) ? entry.OutputDirectory
                : !string.IsNullOrEmpty(output) ? Path.GetDirectoryName(output) : null;
            FolderPath = !string.IsNullOrEmpty(folder) && Directory.Exists(folder) ? folder : null;

            string name = !string.IsNullOrEmpty(entry.ProjectName) ? entry.ProjectName : "Render";
            Title = !string.IsNullOrEmpty(entry.SceneName) && !string.Equals(entry.SceneName, entry.ProjectName, StringComparison.OrdinalIgnoreCase)
                ? $"{name} · {entry.SceneName}" : name;

            var details = new List<string> { FormatDuration(TimeSpan.FromSeconds(entry.DurationSeconds)) };
            if (entry.Width > 0 && entry.Height > 0) details.Add($"{entry.Width}×{entry.Height}");
            if (entry.Samples > 0) details.Add($"{entry.Samples} samples");
            if (!string.IsNullOrEmpty(entry.Engine)) details.Add(entry.Engine);
            DetailText = string.Join(" · ", details);

            string shown = !string.IsNullOrEmpty(output) ? output : entry.OutputDirectory;
            PathText = string.IsNullOrEmpty(shown) ? "Not saved to disk" : shown;
            PathTip = PathText;

            if (!string.IsNullOrEmpty(output) && !outputExists)
                StatusText = previewExists ? "Saved file is missing, opening the preview copy" : "Saved file is missing";
            else if (!outputExists && !previewExists && entry.IsAnimation == false)
                StatusText = "Image no longer available";
        }

        private static string FormatDuration(TimeSpan t)
        {
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
            return $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        }
    }

    public partial class RenderHistoryWindow : Window
    {
        private readonly RenderHistoryStore _store;
        private readonly List<RenderHistoryItem> _items = new List<RenderHistoryItem>();
        private ItemsControl _list;
        private TextBlock _countText, _emptyText;
        private Button _clearAll;
        private readonly Action _changedHandler;

        public RenderHistoryWindow() : this(RenderHistoryStore.Default) { }

        public RenderHistoryWindow(RenderHistoryStore store)
        {
            _store = store;
            AvaloniaXamlLoader.Load(this);
            _list = this.FindControl<ItemsControl>("items");
            _countText = this.FindControl<TextBlock>("countText");
            _emptyText = this.FindControl<TextBlock>("emptyText");
            _clearAll = this.FindControl<Button>("clearAllButton");
            _changedHandler = () => Dispatcher.UIThread.Post(Reload);
            _store.Changed += _changedHandler;
            Closed += (s, e) =>
            {
                _store.Changed -= _changedHandler;
                DisposeThumbnails();
            };
            Reload();
        }

        private void DisposeThumbnails()
        {
            _list.Items = null;
            foreach (var item in _items)
                item.Thumbnail?.Dispose();
            _items.Clear();
        }

        /// <summary>Rebuilds the cards from the store (newest first).</summary>
        public void Reload()
        {
            DisposeThumbnails();
            _items.AddRange(_store.Snapshot().Select(e => new RenderHistoryItem(e, _store)));
            _list.Items = _items.ToList();
            _countText.Text = _items.Count == 1 ? "1 render" : _items.Count + " renders";
            _emptyText.IsVisible = _items.Count == 0;
            _clearAll.IsEnabled = _items.Count > 0;
        }

        private static RenderHistoryItem ItemOf(object sender) => (sender as Control)?.DataContext as RenderHistoryItem;

        private async void OpenImageClicked(object sender, RoutedEventArgs e)
        {
            var item = ItemOf(sender);
            if (item != null) await Open(item.ImageFile, "Open image");
        }

        private async void OpenFolderClicked(object sender, RoutedEventArgs e)
        {
            var item = ItemOf(sender);
            if (item != null) await Open(item.FolderPath, "Open folder");
        }

        private async void ThumbnailDoubleTapped(object sender, RoutedEventArgs e)
        {
            var item = ItemOf(sender);
            if (item?.CanOpenImage == true) await Open(item.ImageFile, "Open image");
        }

        private void DeleteEntryClicked(object sender, RoutedEventArgs e)
        {
            var item = ItemOf(sender);
            if (item != null) _store.Remove(item.Entry.Id);
        }

        private async void ClearAllClicked(object sender, RoutedEventArgs e)
        {
            if (_store.Count == 0) return;
            if (await YesNoWindow.Show(this, "Clear render history", "Remove all entries from the history? The rendered images themselves are not deleted."))
                _store.Clear();
        }

        private async System.Threading.Tasks.Task Open(string path, string title)
        {
            if (string.IsNullOrEmpty(path) || !(File.Exists(path) || Directory.Exists(path)))
            {
                await MessageWindow.Show(this, title, "That file or folder no longer exists.");
                Reload();
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                await MessageWindow.Show(this, title, ex.Message);
            }
        }
    }
}
