using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Objects;
using System;
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
        public string QueueStatus => _queueRunState.Paused ? "Paused - current item may finish" :
            (_queueRunState.ScheduledStart.HasValue ? $"Starts {_queueRunState.ScheduledStart.Value.LocalDateTime:ddd HH:mm} in {FormatDuration(MaxZero(_queueRunState.ScheduledStart.Value - DateTimeOffset.Now))}" :
            (IsQueueing ? "Running" : "Ready"));
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
                if (_queueRunState.TakeScheduledStart(DateTimeOffset.Now))
                {
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
        private void RefreshQueueTools()
        {
            RaisePropertyChanged(QueueStatusProperty, null, QueueStatus);
            RaisePropertyChanged(QueueEtaProperty, null, QueueEta);
        }
        private void PersistQueueRunState()
        {
            BlendFarmSettings.Instance.QueueScheduledStart = _queueRunState.ScheduledStart;
            BlendFarmSettings.Instance.QueuePaused = _queueRunState.Paused;
            BlendFarmSettings.Instance.TrySave();
        }
        public void PauseQueue()
        {
            _queueRunState.Pause();
            PersistQueueRunState();
            RefreshQueueTools();
            AddActivityLog("Queue paused; the current item will finish", ActivityLogType.Warning);
        }
        public async Task PauseQueueNow()
        {
            PauseQueue();
            QueueItem item;
            lock (Queue) item = Queue.FirstOrDefault(candidate => candidate.IsRendering);
            if (item != null) await item.CancelQueueItem();
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
            RefreshQueueTools();
            AddActivityLog($"Updated queued item: {item.Name}");
        }
        public void RetryQueueItem(QueueItem item) { if (item?.ResetForRetry() == true) RefreshQueueName(); }
        public void RetryFailedQueueFrames(QueueItem item) { if (item?.ResetForRetry(true) == true) { RefreshQueueName(); ResumeQueue(); } }
        public void RetryAllFailed()
        {
            int retried = 0;
            lock (Queue) foreach (QueueItem item in Queue) if (item.ResetForRetry()) retried++;
            RefreshQueueName();
            AddActivityLog($"Re-queued {retried} failed item(s)");
        }
        internal void RecordQueueTiming(int frames, TimeSpan elapsed)
        {
            Dispatcher.UIThread.Post(() => { RecordRenderedFrames(frames, elapsed); RefreshQueueTools(); });
        }
    }
}
