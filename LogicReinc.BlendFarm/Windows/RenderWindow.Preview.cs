using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LogicReinc.BlendFarm.Objects;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Windows
{
    // Preview extras: before/after compare and the render history. Kept out of RenderWindow.axaml.cs.
    public partial class RenderWindow
    {
        /// <summary>
        /// Per-project compare data. The previous render is kept as PNG bytes (compact, and a Bitmap that
        /// the UI replaced would get disposed) and only decoded when a new render starts.
        /// </summary>
        private sealed class CompareState
        {
            public bool Hooked;
            /// <summary>The last completed still render, not yet shown as "before".</summary>
            public byte[] SnapshotPng;
            /// <summary>The "before" image of the compare slider.</summary>
            public Bitmap Previous;
        }

        private readonly ConditionalWeakTable<OpenBlenderProject, CompareState> _compareStates = new ConditionalWeakTable<OpenBlenderProject, CompareState>();
        private RenderHistoryWindow _historyWindow;

        private void InitializePreviewFeatures()
        {
            _image.HistoryRequested += ShowRenderHistory;
            //Fires on project switches (and on every tile preview, which is cheap here)
            this.GetObservable(CurrentProjectProperty).Subscribe(project =>
            {
                HookProjectForCompare(project);
                ShowCompareFor(project);
            });
        }

        // ------------------------------------------------------------------ compare

        private CompareState CompareStateFor(OpenBlenderProject project) => _compareStates.GetValue(project, _ => new CompareState());

        private void HookProjectForCompare(OpenBlenderProject project)
        {
            if (project == null) return;
            CompareState state = CompareStateFor(project);
            if (state.Hooked) return;
            state.Hooked = true;
            project.PropertyChanged += (s, e) =>
            {
                //A new render starts: the last completed render becomes the "before" image
                if (e.PropertyName == nameof(OpenBlenderProject.IsRendering) && project.IsRendering)
                    Dispatcher.UIThread.Post(() => PromoteSnapshot(project));
            };
        }

        private void ShowCompareFor(OpenBlenderProject project)
        {
            if (_image == null) return;
            Bitmap previous = project != null && _compareStates.TryGetValue(project, out CompareState state) ? state.Previous : null;
            if (!ReferenceEquals(_image.CompareSource, previous))
                _image.CompareSource = previous;
            if (previous == null && _image.IsCompareEnabled)
                _image.IsCompareEnabled = false;
        }

        /// <summary>Turns the stored snapshot into the project's "before" image.</summary>
        private void PromoteSnapshot(OpenBlenderProject project)
        {
            CompareState state = CompareStateFor(project);
            byte[] png = state.SnapshotPng;
            if (png == null) return;
            state.SnapshotPng = null;
            Task.Run(() =>
            {
                try
                {
                    using (var stream = new MemoryStream(png))
                    {
                        var decoded = new Bitmap(stream);
                        Dispatcher.UIThread.Post(() =>
                        {
                            Bitmap old = state.Previous;
                            state.Previous = decoded;
                            if (old != null) Statics.DisposeReplacedBitmap(old);
                            if (project == CurrentProject) ShowCompareFor(project);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to prepare compare image: {ex.Message}");
                }
            });
        }

        // ------------------------------------------------------------------ history

        private void ShowRenderHistory()
        {
            if (_historyWindow != null)
            {
                _historyWindow.Reload();
                _historyWindow.Activate();
                return;
            }
            _historyWindow = new RenderHistoryWindow();
            _historyWindow.Closed += (s, e) => _historyWindow = null;
            _historyWindow.Show(this);
        }

        /// <summary>
        /// Called for every finished render (single funnel: NotifyRenderCompleted). Adds the history entry
        /// and remembers the image for the compare slider.
        /// </summary>
        private void RecordRenderForPreviewFeatures(RenderCompletionInfo info)
        {
            if (info == null || !info.Success) return;
            //Posted so a queue item's final image has been applied to the project by then
            Dispatcher.UIThread.Post(() =>
            {
                try { CaptureFinishedRender(info); }
                catch (Exception ex) { Console.WriteLine($"Failed to record render history: {ex.Message}"); }
            });
        }

        private void CaptureFinishedRender(RenderCompletionInfo info)
        {
            OpenBlenderProject project = Projects.FirstOrDefault(p => string.Equals(p.BlendFile, info.BlendFile, StringComparison.OrdinalIgnoreCase));
            Bitmap bitmap = project?.LastImage;
            if (bitmap != null && Statics.IsSharedImage(bitmap))
                bitmap = null;

            var entry = new RenderHistoryEntry
            {
                Date = DateTime.Now,
                ProjectName = project?.Name ?? (string.IsNullOrEmpty(info.BlendFile) ? null : Path.GetFileNameWithoutExtension(info.BlendFile)),
                SceneName = info.SceneName,
                BlendFile = info.BlendFile,
                IsAnimation = info.IsAnimation,
                FrameStart = info.FrameStart,
                FrameEnd = info.FrameEnd,
                FrameCount = info.FrameCount,
                DurationSeconds = info.Elapsed.TotalSeconds,
                Width = info.Width,
                Height = info.Height,
                Samples = info.Samples,
                Engine = info.EngineOrError,
                NodeCount = info.NodeCount,
                OutputDirectory = info.OutputDirectory
            };
            bool still = !info.IsAnimation;
            if (still && project != null)
            {
                //A previous completion whose render never started the compare round (eg. it ran as a queue item)
                if (CompareStateFor(project).SnapshotPng != null)
                    PromoteSnapshot(project);
            }

            //Encoding and scaling a large render takes a moment, keep it off the UI thread
            Task.Run(() =>
            {
                byte[] png = null;
                if (bitmap != null)
                {
                    try
                    {
                        using (var stream = new MemoryStream())
                        {
                            bitmap.Save(stream);
                            png = stream.ToArray();
                        }
                    }
                    catch (Exception ex)
                    {
                        //The bitmap was replaced and disposed while encoding
                        Console.WriteLine($"Could not read render for history: {ex.Message}");
                    }
                }

                RenderHistoryStore store = RenderHistoryStore.Default;
                if (png != null)
                {
                    string thumbRel = Path.Combine("thumbs", entry.Id + ".png");
                    string previewRel = Path.Combine("previews", entry.Id + ".jpg");
                    if (RenderHistoryImages.TryCreate(png, store.ResolveAsset(thumbRel), store.ResolveAsset(previewRel), out int w, out int h))
                    {
                        entry.ThumbnailFile = thumbRel;
                        entry.PreviewFile = previewRel;
                        if (entry.Width <= 0 || entry.Height <= 0)
                        {
                            entry.Width = w;
                            entry.Height = h;
                        }
                    }
                }
                store.Add(entry);

                if (still && project != null && png != null)
                    Dispatcher.UIThread.Post(() => CompareStateFor(project).SnapshotPng = png);
            });
        }

        /// <summary>Links a manually saved image to the project's newest still history entry.</summary>
        private void RecordSavedImageForHistory(OpenBlenderProject project, string path)
        {
            if (project == null || string.IsNullOrEmpty(path)) return;
            try
            {
                string full = Path.GetFullPath(path);
                RenderHistoryStore store = RenderHistoryStore.Default;
                var newest = store.FindNewest(e => !e.IsAnimation && string.Equals(e.BlendFile, project.BlendFile, StringComparison.OrdinalIgnoreCase));
                if (newest != null)
                    store.Update(newest.Id, e =>
                    {
                        e.OutputPath = full;
                        e.OutputDirectory = Path.GetDirectoryName(full);
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to link saved image to history: {ex.Message}");
            }
        }
    }
}
