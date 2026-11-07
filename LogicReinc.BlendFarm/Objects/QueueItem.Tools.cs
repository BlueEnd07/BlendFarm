using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Client.Tasks;
using System.Linq;

namespace LogicReinc.BlendFarm.Objects
{
    public partial class QueueItem
    {
        private volatile bool _executing;

        //Live state of the current run, polled by the window's progress panels
        private volatile System.Diagnostics.Stopwatch _runWatch;
        private volatile int[] _runFrames;
        private int _runCompleted;
        private int _runCurrentFrame;
        /// <summary>Elapsed time of the run in progress (zero before it starts).</summary>
        public System.TimeSpan RunElapsed => _runWatch?.Elapsed ?? System.TimeSpan.Zero;
        /// <summary>Frames of the animation being rendered (null for stills).</summary>
        public int[] RunFrames => _runFrames;
        /// <summary>Frames of this animation that are on disk, including ones skipped because they already existed.</summary>
        public int RunCompletedFrames => System.Threading.Volatile.Read(ref _runCompleted);
        public int RunCurrentFrame => System.Threading.Volatile.Read(ref _runCurrentFrame);
        /// <summary>Time spent by earlier runs of this item (it was stopped and resumed), added to the next completion's duration.</summary>
        public System.TimeSpan PriorRunTime { get; private set; }
        /// <summary>Duration of the run that ended last.</summary>
        public System.TimeSpan LastRunElapsed { get; private set; }
        private void BeginRun(System.Diagnostics.Stopwatch watch)
        {
            _runFrames = null;
            System.Threading.Volatile.Write(ref _runCompleted, 0);
            _runWatch = watch;
        }
        private void SetRunFrames(int[] frames, int alreadyDone)
        {
            System.Threading.Volatile.Write(ref _runCompleted, alreadyDone);
            System.Threading.Volatile.Write(ref _runCurrentFrame, frames.Length == 0 ? 0 : frames[System.Math.Min(alreadyDone, frames.Length - 1)]);
            _runFrames = frames;
        }
        private void FrameDone(int frame)
        {
            System.Threading.Interlocked.Increment(ref _runCompleted);
            System.Threading.Volatile.Write(ref _runCurrentFrame, frame);
        }
        private void EndRun(System.TimeSpan elapsed)
        {
            LastRunElapsed = elapsed;
            PriorRunTime += elapsed;
        }
        public bool Animation { get; set; }
        public int[] FrameNumbers { get; set; }
        public int FrameStep { get; set; } = 1;
        public string FrameList { get; set; } = "";
        public int ResolutionScale { get; set; } = 100;
        public bool EncodeMp4 { get; set; }
        public int EncodingFPS { get; set; } = 24;
        public bool? SkipExistingFrames { get; set; }
        public System.DateTime? OutputTimestamp { get; set; }
        public int? OutputFrameStart { get; set; }
        public int? OutputFrameEnd { get; set; }
        internal void ReserveForExecution()
        {
            _executing = true;
            RefreshInfo();
            _owner?.RefreshProgressUi(); //the timing rows switch to this item right away
        }
        private volatile bool _requeueOnCancel;
        /// <summary>Cancelled by the user (not failed, not finished), so it can be put back in the queue.</summary>
        public bool CanRequeue => !_executing && Cancelled && !Completed && string.IsNullOrWhiteSpace(Exception);
        public bool IsFailed => !_executing && !string.IsNullOrWhiteSpace(Exception);
        public string FailedFramesSummary => (Task as AnimationTask)?.FailedFrames.Summary ?? "";
        public bool HasFailedFrames => IsFailed && (Task as AnimationTask)?.FailedFrames.Frames.Count > 0;
        public int[] GetFrameNumbers() => FrameNumbers ?? AnimationFrameSelection.Parse(Settings.Frame, Settings.Frame + Frames - 1).ToArray();
        /// <summary>
        /// Cancels the running item and, once it has unwound, puts it back in the queue (used by "Pause and stop now"),
        /// so it runs again on resume instead of being lost.
        /// </summary>
        public async System.Threading.Tasks.Task CancelAndRequeue()
        {
            _requeueOnCancel = true;
            await CancelQueueItem();
        }
        public void ApplyPendingRequeue()
        {
            if (!_requeueOnCancel) return;
            _requeueOnCancel = false;
            ResetToQueued();
        }
        /// <summary>Returns a cancelled (not failed) item to the queued state. Animations keep their frames and resume.</summary>
        public bool ResetToQueued()
        {
            if (!CanRequeue) return false;
            Cancelled = false;
            FinishedAllFrames = false;
            Task = null;
            //Frames already written by this run are kept, so a stopped animation continues where it left off
            if (IsAnimation) SkipExistingFrames = true;
            RefreshInfo();
            return true;
        }
        public bool ResetForRetry(bool failedFramesOnly = false)
        {
            if (_executing || !IsFailed) return false;
            if (failedFramesOnly)
            {
                int[] failed = (Task as AnimationTask)?.FailedFrames.Frames.Keys.ToArray();
                if (failed == null || failed.Length == 0) return false;
                FrameNumbers = failed;
                Frames = failed.Length;
                FrameList = string.Join(", ", failed);
                Settings.Frame = failed[0];
                EncodeMp4 = false;
                SkipExistingFrames = false;
            }
            Exception = null;
            if (!failedFramesOnly)
            {
                PriorRunTime = System.TimeSpan.Zero;
                OutputTimestamp = null;
                OutputFrameStart = null;
                OutputFrameEnd = null;
            }
            Cancelled = false;
            FinishedAllFrames = false;
            Task = null;
            RefreshInfo();
            return true;
        }
    }
}
