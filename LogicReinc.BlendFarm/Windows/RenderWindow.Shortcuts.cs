using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LogicReinc.BlendFarm.Objects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Windows
{
    // Keyboard shortcuts, the shortcuts help window and .blend drag & drop.
    public partial class RenderWindow
    {
        private Border _dropOverlay;
        private int _dropOverlayToken;

        // Single entry point called from Init().
        private void InitializeUserInputExtras()
        {
            InitializeShortcuts();
            InitializeFileDrop();
            InitializeSystemIntegration();
            InitializeActivityLogTools();
            InitializePresets();
            InitializeOutputWarnings();
        }

        #region Shortcuts

        private void InitializeShortcuts()
        {
            //Tunnel so the shortcuts work wherever the focus is; text-editing keys are never claimed.
            AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);
            Opened += (_, __) => Dispatcher.UIThread.Post(ApplyShortcutTooltips, DispatcherPriority.Loaded);
        }

        private void OnShortcutKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled)
                return;
            ShortcutAction? action = ShortcutCatalog.Resolve(e.Key, e.KeyModifiers);
            if (action == null)
                return;
            if (action == ShortcutAction.CancelRender)
            {
                //Esc also closes an open combo box popup and reverts a text edit: leave it to those controls
                EscapeDecision decision = ShortcutCatalog.DecideEscape(IsRendering, FocusManager.Instance?.Current is TextBox, IsDropDownOpen(), RenderElapsed);
                if (decision == EscapeDecision.Ignore)
                    return;
                if (decision == EscapeDecision.Confirm)
                {
                    e.Handled = true;
                    _ = ConfirmCancelRender();
                    return;
                }
            }
            e.Handled = Run(() => ExecuteShortcut(action.Value));
        }

        private bool _confirmingCancel;

        // A long render is expensive to lose, Esc is easy to hit by accident: ask first.
        private async Task ConfirmCancelRender()
        {
            if (_confirmingCancel)
                return;
            _confirmingCancel = true;
            try
            {
                if (await YesNoWindow.Show(this, "Cancel render", $"The render has been running for {FormatDuration(RenderElapsed)}. Cancel it?") && IsRendering)
                    await CancelRenderSafe();
            }
            catch (Exception ex) { AddActivityLog($"Shortcut failed: {ex.Message}", ActivityLogType.Error); }
            finally { _confirmingCancel = false; }
        }

        /// <summary>Performs a shortcut action; actions that don't apply right now (no project, nothing to save) do nothing.</summary>
        public void ExecuteShortcut(ShortcutAction action)
        {
            bool project = CurrentProject != null;
            switch (action)
            {
                case ShortcutAction.ShowHelp: ShowShortcuts(); break;
                case ShortcutAction.CancelRender: if (IsRendering) _ = CancelRenderSafe(); break;
                case ShortcutAction.OpenProject: _ = RunSafe(OpenProjectDialog); break;
                case ShortcutAction.RenderNow: if (project && !IsRendering) _ = RunSafe(() => Render()); break;
                case ShortcutAction.RenderAnimation: if (project && !IsRendering) RenderAnimation(); break;
                case ShortcutAction.SaveImage: if (HasRenderedImage) _ = RunSafe(SaveImage); break;
                case ShortcutAction.AddToQueue: if (project) _ = RunSafe(AddToQueueNew); break;
                case ShortcutAction.AddAnimationToQueue: if (project) _ = RunSafe(AddAnimationToQueueNew); break;
                case ShortcutAction.CloseProject: if (project && CanCloseProjects) _ = RunSafe(() => CloseProject(CurrentProject)); break;
            }
        }

        // Runs a shortcut action; returns true so the key counts as handled.
        private static bool Run(Action action)
        {
            action();
            return true;
        }

        private bool IsDropDownOpen()
        {
            //Esc closes an open combo box first
            return FocusManager.Instance?.Current is ComboBox combo && combo.IsDropDownOpen;
        }

        private async Task RunSafe(Func<Task> action)
        {
            try { await action(); }
            catch (Exception ex) { AddActivityLog($"Shortcut failed: {ex.Message}", ActivityLogType.Error); }
        }

        private async Task CancelRenderSafe()
        {
            if (IsRendering)
                await RunSafe(CancelRender);
        }

        public void ShowShortcuts() => ShortcutsWindow.ShowFor(this);

        public void ShowShortcutsClicked(object sender, RoutedEventArgs e) => ShowShortcuts();

        // Appends the shortcut to buttons by caption, so the XAML (shared with other work) stays untouched.
        private void ApplyShortcutTooltips()
        {
            var tips = new Dictionary<string, string>
            {
                ["Render Now"] = ShortcutCatalog.Tooltip("Render the current frame", ShortcutCatalog.RenderNow),
                ["Render Animation"] = ShortcutCatalog.Tooltip("Render the frame range", ShortcutCatalog.RenderAnimation),
                ["Cancel"] = ShortcutCatalog.Tooltip("Cancel the render", ShortcutCatalog.Cancel),
                ["Add to Queue"] = ShortcutCatalog.Tooltip("Queue a still render", ShortcutCatalog.AddToQueue),
                ["Add Queue Animation"] = ShortcutCatalog.Tooltip("Queue an animation render", ShortcutCatalog.AddAnimationToQueue),
                ["Save Image"] = ShortcutCatalog.Tooltip("Save the rendered image", ShortcutCatalog.SaveImage),
                ["+"] = ShortcutCatalog.Tooltip("Open a .blend project", ShortcutCatalog.OpenProject),
                ["Help"] = "Open the online help (all keyboard shortcuts: " + ShortcutCatalog.Help + ")",
                ["Shortcuts"] = ShortcutCatalog.Tooltip("Keyboard shortcuts", ShortcutCatalog.Help)
            };
            foreach (Button button in this.GetVisualDescendants().OfType<Button>())
            {
                if (button.Content is string caption && tips.TryGetValue(caption.Trim(), out string tip) &&
                    ToolTip.GetTip(button) == null)
                    ToolTip.SetTip(button, tip);
            }
        }

        #endregion

        #region Drag & drop of .blend files

        private void InitializeFileDrop()
        {
            //The window is the drop target for files; node cards already allow drops for node re-ordering
            //and their events bubble up to the handlers below (which only react to file data).
            DragDrop.SetAllowDrop(this, true);

            Grid root = this.FindControl<Grid>("windowColumns");
            _dropOverlay = DropOverlay.Create("Drop a .blend file to open it");
            Grid.SetColumnSpan(_dropOverlay, Math.Max(1, root.ColumnDefinitions.Count));
            Grid.SetRowSpan(_dropOverlay, Math.Max(1, root.RowDefinitions.Count));
            root.Children.Add(_dropOverlay);

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
            //Moving between child controls fires leave+over in a row: only hide if no new over follows
            int token = ++_dropOverlayToken;
            Dispatcher.UIThread.Post(() =>
            {
                if (token == _dropOverlayToken)
                    _dropOverlay.IsVisible = false;
            }, DispatcherPriority.Background);
        }

        private async void FileDropped(object sender, DragEventArgs e)
        {
            _dropOverlayToken++;
            _dropOverlay.IsVisible = false;
            if (!HasFiles(e))
                return;
            e.Handled = true;
            List<string> files = e.Data.GetFileNames()?.ToList() ?? new List<string>();
            try
            {
                await OpenDroppedFiles(files);
            }
            catch (Exception ex)
            {
                AddActivityLog($"Could not open dropped file: {ex.Message}", ActivityLogType.Error);
            }
        }

        /// <summary>Opens the .blend files among the given paths as projects (the same flow as the + button).</summary>
        public async Task OpenDroppedFiles(IEnumerable<string> paths)
        {
            List<string> all = paths?.Select(Statics.SanitizePath).ToList() ?? new List<string>();
            List<string> blends = DroppedFiles.BlendFiles(all);
            if (blends.Count == 0)
            {
                if (all.Count > 0)
                    AddActivityLog($"Ignored {Path.GetFileName(all[0])}: only .blend files can be opened", ActivityLogType.Warning);
                return;
            }

            OpenBlenderProject lastOpened = null;
            bool lastIsNew = false;
            foreach (string path in blends)
            {
                if (!File.Exists(path))
                {
                    AddActivityLog($"Dropped file not found: {path}", ActivityLogType.Warning);
                    continue;
                }
                string full = Path.GetFullPath(path);
                OpenBlenderProject existing = Projects.FirstOrDefault(p => DroppedFiles.SamePath(p.BlendFile, full));
                if (existing != null)
                {
                    lastOpened = existing;
                    lastIsNew = false;
                    AddActivityLog($"{existing.BlendFileDisplay} is already open");
                    continue;
                }
                lastOpened = LoadProject(full);
                lastIsNew = true;
                RememberRecentProject(full);
                AddActivityLog($"Opened {lastOpened.BlendFileDisplay} (dropped)", ActivityLogType.Success);
            }
            if (lastOpened == null)
                return;

            await SwitchProject(lastOpened);
            if (lastIsNew)
                await PeekDroppedProject(lastOpened);
        }

        // Same sync + peek the startup flow does, so resolution, frame range, scenes and cameras are filled in.
        private async Task PeekDroppedProject(OpenBlenderProject project)
        {
            if (Manager == null || !(Options?.ImportSettings ?? true))
                return;
            bool nodesReady = Manager.Nodes != null && Manager.Nodes.Any(x => x.Connected);
            if (!nodesReady)
            {
                AddActivityLog("Connect a node, then use Import Settings to read this project's scene settings");
                return;
            }
            if (IsRendering || IsQueueing)
            {
                AddActivityLog("Render in progress: use Import Settings later to read this project's scene settings");
                return;
            }
            try
            {
                await SyncAll();
                if (CurrentProject == project && Manager.Nodes.Any(x => x.Connected && x.IsSessionSynced(project.SessionID)))
                    await ImportSettings();
            }
            catch (Exception ex)
            {
                AddActivityLog($"Reading {project.BlendFileDisplay} failed: {ex.Message}", ActivityLogType.Error);
            }
        }

        // Keeps the "recent projects" list of the project picker up to date, like picking a file there does.
        private static void RememberRecentProject(string path)
        {
            BlendFarmSettings settings = BlendFarmSettings.Instance;
            settings.History ??= new List<BlendFarmSettings.HistoryEntry>();
            var existing = settings.History.FirstOrDefault(x => DroppedFiles.SamePath(x.Path, path));
            if (existing != null)
                settings.History.Remove(existing);
            settings.History.Add(new BlendFarmSettings.HistoryEntry { Name = Path.GetFileName(path), Path = path, Date = DateTime.Now });
            settings.History = settings.History.OrderByDescending(x => x.Date).Take(10).ToList();
            settings.TrySave();
        }

        #endregion
    }
}
