using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LogicReinc.BlendFarm.Objects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Windows
{
    // Window presentation and persistence live here so render/scheduling code stays independent.
    public partial class RenderWindow
    {
        public static readonly DirectProperty<RenderWindow, bool> HasCurrentProjectProperty =
            AvaloniaProperty.RegisterDirect<RenderWindow, bool>(nameof(HasCurrentProject), w => w.HasCurrentProject);
        public static readonly DirectProperty<RenderWindow, bool> HasRenderedImageProperty =
            AvaloniaProperty.RegisterDirect<RenderWindow, bool>(nameof(HasRenderedImage), w => w.HasRenderedImage);
        public static readonly DirectProperty<RenderWindow, bool> CanCloseProjectsProperty =
            AvaloniaProperty.RegisterDirect<RenderWindow, bool>(nameof(CanCloseProjects), w => w.CanCloseProjects);
        public static readonly DirectProperty<RenderWindow, bool> CanOpenOutputFolderProperty =
            AvaloniaProperty.RegisterDirect<RenderWindow, bool>(nameof(CanOpenOutputFolder), w => w.CanOpenOutputFolder);

        public bool HasCurrentProject => CurrentProject != null;
        public bool HasRenderedImage => CurrentProject?.LastImage != null;
        // Also covers the sync phase, before a project has a CurrentTask, and the live-render loop.
        public bool CanCloseProjects => !IsRendering && !IsLiveChanging && !IsQueueing;
        public bool CanOpenOutputFolder => GetOutputFolder() != null;

        private readonly Dictionary<string, string> _projectOutputFolders = new Dictionary<string, string>();
        private Rect _normalWindowBounds;
        private bool _layoutReady;
        private bool _closingProject;

        private void InitializeWindowLayout()
        {
            var settings = BlendFarmSettings.Instance;
            settings.ExpandedRenderSections ??= new Dictionary<string, bool>();
            foreach (string key in new[] { "Output", "Frames", "Scene", "Engine", "Live", "Network", "Notifications" })
            {
                var expander = this.FindControl<Expander>("section" + key);
                expander.IsExpanded = settings.ExpandedRenderSections.TryGetValue(key, out bool expanded)
                    ? expanded : key == "Output" || key == "Frames";
                expander.GetObservable(Expander.IsExpandedProperty).Subscribe(value =>
                {
                    if (!_layoutReady) return;
                    settings.ExpandedRenderSections[key] = value;
                    settings.TrySave();
                });
            }

            Opened += (_, __) => RestoreWindowLayout();
            PositionChanged += (_, __) => RememberNormalBounds();
            this.GetObservable(ClientSizeProperty).Subscribe(_ => RememberNormalBounds());
            Closing += (_, __) => SaveWindowLayout();
            UpdatePresentationState();
        }

        private void UpdatePresentationState()
        {
            RaisePropertyChanged(HasCurrentProjectProperty, !HasCurrentProject, HasCurrentProject);
            RaisePropertyChanged(HasRenderedImageProperty, !HasRenderedImage, HasRenderedImage);
            RaisePropertyChanged(CanCloseProjectsProperty, !CanCloseProjects, CanCloseProjects);
            RaisePropertyChanged(CanOpenOutputFolderProperty, !CanOpenOutputFolder, CanOpenOutputFolder);
        }

        private async void CloseProjectClicked(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!CanCloseProjects || sender is not Control control || control.DataContext is not OpenBlenderProject project)
                return;
            await CloseProject(project);
        }

        // Shared by the tab's close button and the Ctrl+W shortcut.
        private async Task CloseProject(OpenBlenderProject project)
        {
            if (!CanCloseProjects || project == null)
                return;
            int index = Projects.IndexOf(project);
            if (index < 0 || project.IsRendering) return;
            //Queued items keep working on the project after its tab is gone: make sure that is what the user wants
            int queued;
            lock (Queue)
                queued = Queue.Count(item => ReferenceEquals(item.Project, project) && item.Active);
            if (queued > 0)
            {
                bool close = await YesNoWindow.Show(this, "Close project",
                    $"{queued} queued item(s) still use {project.BlendFileDisplay}. They stay in the queue and render when it runs, but the project's tab and preview image are removed. Close it anyway?");
                //Things may have changed while the dialog was open
                index = Projects.IndexOf(project);
                if (!close || !CanCloseProjects || index < 0 || project.IsRendering) return;
            }
            // Suppress selection events while the collection and selected index change together.
            _closingProject = true;
            try
            {
                Projects.Remove(project);
                if (CurrentProject == project)
                    await SwitchProject(Projects.Count == 0 ? null : Projects[Math.Min(index, Projects.Count - 1)]);
                else
                    await SwitchProject(CurrentProject);
            }
            finally { _closingProject = false; }
            ReleaseClosedProjectState(project);
            AddActivityLog($"Closed {project.BlendFileDisplay}");
        }

        private string GetOutputFolder()
        {
            if (CurrentProject == null) return null;
            //The in-memory value wins, the persisted one covers the first look after a restart
            if (_projectOutputFolders.TryGetValue(CurrentProject.BlendFile, out string folder) && Directory.Exists(folder))
                return folder;
            var saved = BlendFarmSettings.Instance.LastOutputFolders;
            return saved != null && saved.TryGetValue(CurrentProject.BlendFile, out folder) && Directory.Exists(folder) ? folder : null;
        }

        private void RememberOutputFolder(OpenBlenderProject project, string folder)
        {
            if (project == null || string.IsNullOrEmpty(folder)) return;
            _projectOutputFolders[project.BlendFile] = folder;
            var settings = BlendFarmSettings.Instance;
            settings.LastOutputFolders ??= new Dictionary<string, string>();
            if (!settings.LastOutputFolders.TryGetValue(project.BlendFile, out string stored) || stored != folder)
            {
                settings.LastOutputFolders[project.BlendFile] = folder;
                settings.TrySave();
            }
            _toastFolder = folder;
            UpdatePresentationState();
        }

        public async void OpenOutputFolder()
        {
            string folder = GetOutputFolder();
            if (folder == null) return;
            try { OpenUrl(folder); }
            catch (Exception ex) { await MessageWindow.Show(this, "Open output folder", ex.Message); }
        }

        private static double ValidSize(double value, double fallback, double min, double max) =>
            double.IsFinite(value) && value >= min && value <= max ? value : fallback;

        private void RestoreWindowLayout()
        {
            var layout = BlendFarmSettings.Instance.WindowLayout ?? new BlendFarmSettings.RenderWindowLayout();
            var screen = Screens.All.FirstOrDefault(s => layout.X.HasValue && layout.Y.HasValue &&
                s.WorkingArea.Contains(new PixelPoint(layout.X.Value, layout.Y.Value))) ?? Screens.Primary;
            if (screen != null)
            {
                var area = screen.WorkingArea;
                double scale = screen.PixelDensity;
                // Position is the outer frame's origin; leave room for native window decorations.
                double frameWidth = Math.Max(0, (FrameSize?.Width ?? ClientSize.Width) - ClientSize.Width);
                double frameHeight = Math.Max(0, (FrameSize?.Height ?? ClientSize.Height) - ClientSize.Height);
                double usableWidth = Math.Max(1, area.Width / scale - frameWidth);
                double usableHeight = Math.Max(1, area.Height / scale - frameHeight);
                // All three panels must remain reachable, even on a smaller monitor.
                MinWidth = Math.Min(1050, usableWidth);
                MinHeight = Math.Min(600, usableHeight);
                Width = Math.Clamp(ValidSize(layout.Width, 1400, 500, 12000), MinWidth, usableWidth);
                Height = Math.Clamp(ValidSize(layout.Height, 975, 300, 12000), MinHeight, usableHeight);
                int outerWidth = (int)Math.Ceiling((Width + frameWidth) * scale);
                int outerHeight = (int)Math.Ceiling((Height + frameHeight) * scale);
                int x = layout.X ?? area.X + (area.Width - outerWidth) / 2;
                int y = layout.Y ?? area.Y + (area.Height - outerHeight) / 2;
                Position = new PixelPoint(Math.Clamp(x, area.X, Math.Max(area.X, area.Right - outerWidth)),
                    Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - outerHeight)));
            }
            var columns = this.FindControl<Grid>("windowColumns").ColumnDefinitions;
            double left = ValidSize(layout.LeftWidth, 380, 300, 680);
            double right = ValidSize(layout.RightWidth, 460, 390, 800);
            double available = Math.Max(690, Width - 252);
            double excess = Math.Max(0, left + right - available);
            left -= Math.Min(left - 300, excess);
            right = Math.Max(390, Math.Min(right, available - left));
            columns[0].Width = new GridLength(left);
            columns[4].Width = new GridLength(right);
            this.FindControl<Grid>("previewRows").RowDefinitions[3].Height = new GridLength(
                Math.Min(ValidSize(layout.ActivityHeight, 280, 170, 3000), Math.Max(170, Height - 300)));
            _layoutReady = true;
            RememberNormalBounds();
            if (layout.Maximized) WindowState = WindowState.Maximized;
        }

        private void RememberNormalBounds()
        {
            if (_layoutReady && WindowState == WindowState.Normal && ClientSize.Width > 0 && ClientSize.Height > 0)
                _normalWindowBounds = new Rect(Position.X, Position.Y, ClientSize.Width, ClientSize.Height);
        }

        private void SaveWindowLayout()
        {
            if (!_layoutReady) return;
            // A final resize/move can arrive between layout notifications and Closing.
            RememberNormalBounds();
            var columns = this.FindControl<Grid>("windowColumns").ColumnDefinitions;
            BlendFarmSettings.Instance.WindowLayout = new BlendFarmSettings.RenderWindowLayout
            {
                Width = _normalWindowBounds.Width,
                Height = _normalWindowBounds.Height,
                X = (int)_normalWindowBounds.X,
                Y = (int)_normalWindowBounds.Y,
                Maximized = WindowState == WindowState.Maximized,
                LeftWidth = columns[0].ActualWidth,
                RightWidth = columns[4].ActualWidth,
                ActivityHeight = this.FindControl<Grid>("previewRows").RowDefinitions[3].ActualHeight
            };
            BlendFarmSettings.Instance.TrySave();
        }
    }
}

