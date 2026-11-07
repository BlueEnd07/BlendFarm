using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Client.Tasks;
using LogicReinc.BlendFarm.Objects;
using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Windows
{
    public partial class RenderWindow
    {
        private StackPanel _failedFramesPanel;
        private void AnimationOptionsChanged(object sender, Avalonia.Interactivity.RoutedEventArgs args)
        {
            if (CurrentProject != null) PersistAnimationSettings(CurrentProject);
        }
        private void PersistAnimationSettings(OpenBlenderProject project)
        {
            BlendFarmSettings.Instance.ApplyProjectSettings(project.BlendFile, project.GetProjectSettings());
            BlendFarmSettings.Instance.TrySave();
        }
        private void UpdateSelectedAnimationProgress(TimeSpan elapsed, IReadOnlyList<int> frames, int completed, int currentFrame)
        {
            double progress = Math.Min(1, completed / (double)frames.Count);
            _animationFramesRendered.Text = $"{completed} / {frames.Count}";
            _animationCurrentFrame.Text = completed >= frames.Count ? "Done" : currentFrame.ToString();
            _animationEstimatedRemaining.Text = GetEstimatedRemainingText(elapsed, progress);
            _animationTimelineProgress.Value = progress * 100;
            _animationTimeline.Text = BuildFrameTimeline(frames.First(), frames.Last(), currentFrame, progress);
            UpdateCenterRenderProgress(progress);
        }
        private void ShowAnimationFailures(FailedFrameReport report, Action retry)
        {
            var failed = report.Frames;
            if (failed.Count == 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                string summary = report.Summary;
                string nodes = string.Join(", ", failed.Where(pair => !string.IsNullOrEmpty(pair.Value)).Select(pair => $"{pair.Key} on {pair.Value}"));
                AddActivityLog(summary + (nodes.Length == 0 ? "" : " (" + nodes + ")"), ActivityLogType.Error);
                if (!(_animationProgressPanel?.Child is Panel parent)) return;
                if (_failedFramesPanel != null) parent.Children.Remove(_failedFramesPanel);
                _failedFramesPanel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 0) };
                _failedFramesPanel.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.OrangeRed });
                var button = new Button { Content = "Re-render failed frames" };
                button.Click += (_, __) =>
                {
                    if (IsRendering) { AddActivityLog("Wait for the current render before retrying frames.", ActivityLogType.Warning); return; }
                    retry();
                    _failedFramesPanel.IsVisible = false;
                };
                _failedFramesPanel.Children.Add(button);
                parent.Children.Add(_failedFramesPanel);
                _animationProgressPanel.IsVisible = true;
            });
        }
        internal void ShowQueueAnimationFailures(QueueItem item)
        {
            if (item.Task is AnimationTask animation)
                ShowAnimationFailures(animation.FailedFrames, () => RetryFailedQueueFrames(item));
        }
        private void ShowManualAnimationFailures(AnimationTask animation, OpenBlenderProject project, RenderManagerSettings settings, string pattern, string outputDir, DateTime timestamp)
        {
            animation.FinishFailedFrameReport(animation.Cancelled || _renderCancelRequested);
            int[] failed = animation.FailedFrames.Frames.Keys.ToArray();
            if (failed.Length == 0) return;
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<RenderManagerSettings>(System.Text.Json.JsonSerializer.Serialize(settings));
            ShowAnimationFailures(animation.FailedFrames, () =>
            {
                snapshot.Frame = failed[0];
                var item = new QueueItem(this, project, snapshot, outputDir, failed.Length)
                {
                    Animation = true, FrameNumbers = failed, FrameList = string.Join(", ", failed), FrameFormat = pattern, SkipExistingFrames = false, OutputTimestamp = timestamp,
                    OutputFrameStart = animation.StartFrame, OutputFrameEnd = animation.EndFrame
                };
                lock (Queue) Queue.Add(item);
                RefreshQueueName();
                ResumeQueue();
            });
        }
        private OutputNameContext BuildAnimationOutputContext(OpenBlenderProject project, RenderManagerSettings settings, int frame, int firstFrame, int lastFrame, DateTime timestamp)
        {
            OutputNameContext context = BuildOutputContext(project, settings, frame, timestamp);
            context.StartFrame = firstFrame;
            context.EndFrame = lastFrame;
            return context;
        }
        internal async Task EncodeAnimationAsync(OpenBlenderProject project, string pattern, string outputDir, string[] framePaths, int fps)
        {
            string configured = BlendFarmSettings.Instance.FfmpegPath;
            await Task.Run(async () =>
            {
                string manifest = null;
                void Log(string message, ActivityLogType type = ActivityLogType.Info) => Dispatcher.UIThread.Post(() => AddActivityLog(message, type));
                try
                {
                    string executable = FindFfmpeg(configured);
                    if (executable == null)
                    {
                        Log("MP4 not encoded: ffmpeg was not found. Install FFmpeg from ffmpeg.org, add it to PATH, or set its executable path in Frames.", ActivityLogType.Warning);
                        return;
                    }
                    if (framePaths.Any(path => !File.Exists(path))) throw new IOException("Some rendered frames are missing; MP4 encoding skipped.");
                    manifest = Path.Combine(Path.GetTempPath(), "blendfarm-" + Guid.NewGuid().ToString("N") + ".txt");
                    await File.WriteAllTextAsync(manifest, AnimationEncoding.BuildManifest(framePaths), new UTF8Encoding(false));
                    string stem = Regex.Replace(Path.GetFileNameWithoutExtension(pattern), "#+", "").Trim(' ', '-', '_', '.');
                    if (string.IsNullOrWhiteSpace(stem) || stem.Contains("{") || stem.Contains("}")) stem = project.Name;
                    string outputPath = Path.Combine(outputDir, stem + ".mp4");
                    var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                    foreach (string argument in AnimationEncoding.BuildArguments(manifest, outputPath, fps)) start.ArgumentList.Add(argument);
                    Log($"Encoding MP4 at {fps} fps: {outputPath}");
                    using (var process = Process.Start(start))
                    {
                        string line;
                        string last = "";
                        DateTime lastLog = DateTime.MinValue;
                        while ((line = await process.StandardError.ReadLineAsync()) != null)
                        {
                            last = line;
                            if (line.Contains("frame=") && DateTime.UtcNow - lastLog > TimeSpan.FromSeconds(2))
                            {
                                Log("FFmpeg: " + line);
                                lastLog = DateTime.UtcNow;
                            }
                        }
                        await process.WaitForExitAsync();
                        if (process.ExitCode != 0) throw new IOException($"FFmpeg exited with code {process.ExitCode}: {last}");
                    }
                    Log("MP4 encoded: " + outputPath, ActivityLogType.Success);
                }
                catch (Exception ex) { Log("MP4 encoding failed: " + ex.Message, ActivityLogType.Error); }
                finally
                {
                    if (manifest != null) try { File.Delete(manifest); } catch (IOException) { }
                }
            });
        }
        private static string FindFfmpeg(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? Path.GetFullPath(configured) : null;
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                string path = Path.Combine(directory.Trim().Trim('"'), OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
                if (File.Exists(path)) return path;
            }
            return null;
        }
    }
}
