using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LogicReinc.BlendFarm.Objects;
using System.Linq;

namespace LogicReinc.BlendFarm.Windows
{
    // Drop a .blend file on the project picker to fill the file box.
    public partial class ProjectWindow
    {
        private Border _dropOverlay;
        private int _dropOverlayToken;

        private void InitializeFileDrop()
        {
            //Wrap the existing content so a highlight can cover the whole window
            var original = Content as Control;
            if (original == null)
                return;
            Content = null;
            var root = new Grid();
            root.Children.Add(original);
            _dropOverlay = DropOverlay.Create("Drop a .blend file to select it");
            root.Children.Add(_dropOverlay);
            Content = root;

            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, FileDragOver);
            AddHandler(DragDrop.DragLeaveEvent, FileDragLeave);
            AddHandler(DragDrop.DropEvent, FileDropped);
        }

        private static bool HasFiles(DragEventArgs e) => e.Data != null && e.Data.Contains(DataFormats.FileNames);

        private void FileDragOver(object sender, DragEventArgs e)
        {
            if (!HasFiles(e))
                return;
            bool anyBlend = DroppedFiles.BlendFiles(e.Data.GetFileNames()).Count > 0;
            e.DragEffects = anyBlend ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            _dropOverlayToken++;
            _dropOverlay.IsVisible = anyBlend;
        }

        private void FileDragLeave(object sender, RoutedEventArgs e)
        {
            int token = ++_dropOverlayToken;
            Dispatcher.UIThread.Post(() =>
            {
                if (token == _dropOverlayToken)
                    _dropOverlay.IsVisible = false;
            }, DispatcherPriority.Background);
        }

        private void FileDropped(object sender, DragEventArgs e)
        {
            _dropOverlayToken++;
            _dropOverlay.IsVisible = false;
            if (!HasFiles(e))
                return;
            e.Handled = true;
            string file = DroppedFiles.BlendFiles(e.Data.GetFileNames()?.Select(Statics.SanitizePath)).FirstOrDefault();
            if (file != null)
                fileSelection.Text = file;
        }
    }
}
