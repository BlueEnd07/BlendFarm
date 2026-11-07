using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Objects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Windows
{
    public partial class RenderWindow
    {
        private readonly QueueRunState _queueRunState = new QueueRunState();
        private DispatcherTimer _queueToolsTimer;
        private static readonly DirectProperty<RenderWindow, string> QueueStatusProperty =
            AvaloniaProperty.RegisterDirect<RenderWindow, string>(nameof(QueueStatus), window => window.QueueStatus);
        private static readonly DirectProperty<RenderWindow, string> QueueEtaProperty =
            AvaloniaProperty.RegisterDirect<RenderWindow, string>(nameof(QueueEta), window => window.QueueEta);
        private readonly DateTime _windowCreatedUtc = DateTime.UtcNow;
        private Task _nodeSetupTask;
        private bool _loggedScheduleNodeWait;
        public string QueueStatus => _queueRunState.Paused ? (IsQueueing ? "Paused - current item may finish" : "Paused") :
            (_queueRunState.ScheduledStart.HasValue && _queueRunState.ScheduledStart.Value <= DateTimeOffset.Now ? "Start is due – waiting for render nodes…" :
            _queueRunState.ScheduledStart.HasValue ? $"Starts {_queueRunState.ScheduledStart.Value.LocalDateTime:ddd HH:mm} in {FormatDuration(MaxZero(_queueRunState.ScheduledStart.Value - DateTimeOffset.Now))}" :
            (_queueWaitingForNodes ? "Waiting for render nodes\u2026" :
            (IsQueueing ? "Running" : "Ready")));
        public string QueueEta
        {
            get
            {
                double[] frames;
                lock (Queue) frames = Queue.Where(item => item.Active).Select(item => Math.Max(0, item.Frames * (1 - item.Progress))).ToArray();
                TimeSpan? estimate = QueueEstimate.Remaining(frames, _totalFrameRenderTime, _timedFrameCount);
                return "Total ETA: " + (estimate.HasValue ? FormatDuration(estimate.Value) : "\u2014");
            }
        }
        public string FfmpegPath
        {
            get => BlendFarmSettings.Instance.FfmpegPath;
            set { BlendFarmSettings.Instance.FfmpegPath = value ?? ""; BlendFarmSettings.Instance.TrySave(); }
        }
        private static TimeSpan MaxZero(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
        private void QueueToolsAttached(object sender, VisualTreeAttachmentEventArgs args)
        {
            if (_queueToolsTimer != null || Design.IsDesignMode) return;
            _queueToolsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _queueToolsTimer.Tick += (_, __) =>
            {
                //An overdue schedule right after launch waits for automatic node setup instead of failing every item
                if (_queueRunState.ScheduledStart.HasValue && _queueRunState.ScheduledStart.Value <= DateTimeOffset.Now && DelayScheduledStartForNodes())
                {
                    if (!_loggedScheduleNodeWait)
                    {
                        _loggedScheduleNodeWait = true;
                        AddActivityLog("Scheduled start is due; waiting for render nodes to connect first", ActivityLogType.Warning);
                    }
                }
                else if (_queueRunState.TakeScheduledStart(DateTimeOffset.Now))
                {
                    _loggedScheduleNodeWait = false;
                    PersistQueueRunState();
                    AddActivityLog("Scheduled queue start", ActivityLogType.Success);
                    StartQueueingProcess();
                }
                RefreshQueueTools();
            };
            Closed += (_, __) => _queueToolsTimer.Stop();
            _queueToolsTimer.Start();
            Dispatcher.UIThread.Post(() =>
            {
                var settings = BlendFarmSettings.Instance;
                if (settings.QueueScheduledStart.HasValue)
                {
                    LoadQueue();
                    _queueRunState.Schedule(settings.QueueScheduledStart.Value);
                }
                if (settings.QueuePaused) _queueRunState.Pause();
                RefreshQueueTools();
            });
        }
        private bool DelayScheduledStartForNodes()
        {
            if (Manager == null) return false;
            var (usable, pending) = Manager.CountRenderNodes();
            bool setupRunning = _nodeSetupTask != null && !_nodeSetupTask.IsCompleted;
            return QueueNodeWait.ShouldDelayScheduledStart(usable, pending, setupRunning, DateTime.UtcNow - _windowCreatedUtc);
        }
        //Persist the queue after any change while a schedule is pending, so the scheduled run matches what the user sees
        internal void QueueMutated()
        {
            RefreshQueueName();
            if (!_queueRunState.ScheduledStart.HasValue) return;
            List<QueueItemDTO> dtos;
            lock (Queue) dtos = Queue.Where(item => item.IsQueued).Select(item => item.ToDTO()).ToList();
            BlendFarmSettings.Instance.RenderQueue = dtos;
            BlendFarmSettings.Instance.TrySave();
        }
        //Starts the queue after items were added, without overriding a deliberate pause or a pending schedule
        private void StartQueueIfAllowed(string what)
        {
            if (_queueRunState.Paused) { AddActivityLog($"{what} added to queue (queue paused)"); return; }
            if (_queueRunState.ScheduledStart.HasValue) { AddActivityLog($"{what} added to queue (starts {_queueRunState.ScheduledStart.Value.LocalDateTime:ddd HH:mm})"); return; }
            StartQueueingProcess();
        }
        private void RefreshQueueTools()
        {
            RaisePropertyChanged(QueueStatusProperty, null, QueueStatus);
            RaisePropertyChanged(QueueEtaProperty, null, QueueEta);
            RefreshProgressUi();
        }

        private QueueItem _progressQueueItem;
        /// <summary>
        /// Keeps the timing rows, the progress summary and the pinned animation panel truthful for queue items,
        /// which don't go through the manual render's own progress callbacks. Also resets stale numbers when idle.
        /// </summary>
        internal void RefreshProgressUi()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RefreshProgressUi); return; }
            QueueItem item;
            lock (Queue) item = Queue.FirstOrDefault(candidate => candidate.IsRendering);
            if (item != null)
            {
                if (!ReferenceEquals(_progressQueueItem, item) && _failedFramesPanel != null)
                    _failedFramesPanel.IsVisible = false; //a new run: the previous run's failure report is history
                _progressQueueItem = item;
                TimeSpan elapsed = item.RunElapsed;
                double progress = item.IsAnimation && item.RunFrames != null
                    ? Math.Min(1, item.RunCompletedFrames / (double)Math.Max(1, item.RunFrames.Length))
                    : item.Progress;
                if (_lastRenderTime != null) _lastRenderTime.Text = FormatDuration(elapsed);
                if (_estimatedRenderTime != null) _estimatedRenderTime.Text = GetEstimatedRemainingText(elapsed, progress);
                if (item.IsAnimation && item.RunFrames != null && _animationProgressPanel != null && _animationFramesRendered != null)
                {
                    SetAnimationRowsVisible(true);
                    _animationProgressPanel.IsVisible = true;
                    UpdateSelectedAnimationProgress(elapsed, item.RunFrames, item.RunCompletedFrames, item.RunCurrentFrame);
                }
                UpdateCenterRenderProgress(progress);
                return;
            }
            if (_progressQueueItem != null)
            {
                //The queue item that was driving the rows just ended: leave the final state behind
                QueueItem ended = _progressQueueItem;
                _progressQueueItem = null;
                if (_lastRenderTime != null) _lastRenderTime.Text = FormatDuration(ended.LastRunElapsed);
                if (_estimatedRenderTime != null)
                    _estimatedRenderTime.Text = ended.Completed ? "Done" : ended.IsFailed ? "Failed" : "Stopped";
                HideAnimationProgress();
            }
            if (!IsRendering)
                UpdateCenterRenderProgress(0);
        }
        private void PersistQueueRunState()
        {
            BlendFarmSettings.Instance.QueueScheduledStart = _queueRunState.ScheduledStart;
            BlendFarmSettings.Instance.QueuePaused = _queueRunState.Paused;
            BlendFarmSettings.Instance.TrySave();
        }
        public void PauseQueue() => PauseQueueCore("Queue paused; the current item will finish");
        private void PauseQueueCore(string message)
        {
            _queueRunState.Pause();
            PersistQueueRunState();
            RefreshQueueTools();
            AddActivityLog(message, ActivityLogType.Warning);
        }
        public async Task PauseQueueNow()
        {
            QueueItem item;
            lock (Queue) item = Queue.FirstOrDefault(candidate => candidate.IsRendering);
            //Back to queued afterwards, so the item runs again on resume instead of being lost
            PauseQueueCore(item != null
                ? $"Queue paused; stopped '{item.Name}' and put it back in the queue"
                : "Queue paused; nothing was rendering");
            if (item != null) await item.CancelAndRequeue();
        }
        public void ResumeQueue()
        {
            _queueRunState.Resume();
            PersistQueueRunState();
            RefreshQueueTools();
            StartQueueingProcess();
        }
        public async Task ScheduleQueue()
        {
            DateTimeOffset? start = await new ScheduleWindow().ShowDialog<DateTimeOffset?>(this);
            if (!start.HasValue) return;
            _queueRunState.Schedule(start.Value);
            SaveQueue();
            PersistQueueRunState();
            RefreshQueueTools();
            AddActivityLog($"Queue scheduled for {start.Value.LocalDateTime:f}. Keep BlendFarm open.");
        }
        public void CancelQueueSchedule()
        {
            _queueRunState.CancelSchedule();
            _queueRunState.Pause();
            PersistQueueRunState();
            RefreshQueueTools();
            AddActivityLog("Queue schedule cancelled; queue paused", ActivityLogType.Warning);
        }
        public async Task EditQueueItem(QueueItem item)
        {
            if (item == null || !item.IsQueued) return;
            QueueItemDTO edited = await new QueueItemEditWindow(item).ShowDialog<QueueItemDTO>(this);
            if (edited == null || !item.IsQueued) return;
            lock (Queue)
            {
                if (!item.IsQueued) return;
                int index = Queue.IndexOf(item);
                if (index < 0) return;
                Queue[index] = QueueItem.FromDTO(this, item.Project, edited);
            }
            QueueMutated();
            RefreshQueueTools();
            AddActivityLog($"Updated queued item: {item.Name}");
        }
        public void RetryQueueItem(QueueItem item) { if (item?.ResetForRetry() == true) QueueMutated(); }
        public void RequeueQueueItem(QueueItem item)
        {
            if (item?.ResetToQueued() != true) return;
            QueueMutated();
            AddActivityLog($"Requeued {item.Name}");
        }
        public void RetryFailedQueueFrames(QueueItem item)
        {
            if (item?.ResetForRetry(true) != true) return;
            QueueMutated();
            StartQueueIfAllowed($"Failed frames of {item.Name}");
        }
        public void RetryAllFailed()
        {
            int retried = 0;
            lock (Queue) foreach (QueueItem item in Queue) if (item.ResetForRetry()) retried++;
            QueueMutated();
            AddActivityLog($"Re-queued {retried} failed item(s)");
        }
        internal void RecordQueueTiming(int frames, TimeSpan elapsed)
        {
            Dispatcher.UIThread.Post(() => { RecordRenderedFrames(frames, elapsed); RefreshQueueTools(); });
        }
    }
}
