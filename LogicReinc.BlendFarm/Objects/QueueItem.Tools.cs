using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Client.Tasks;
using System.Linq;

namespace LogicReinc.BlendFarm.Objects
{
    public partial class QueueItem
    {
        private volatile bool _executing;
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
