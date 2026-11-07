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
        public bool IsFailed => !_executing && !string.IsNullOrWhiteSpace(Exception);
        public string FailedFramesSummary => (Task as AnimationTask)?.FailedFrames.Summary ?? "";
        public bool HasFailedFrames => IsFailed && (Task as AnimationTask)?.FailedFrames.Frames.Count > 0;
        public int[] GetFrameNumbers() => FrameNumbers ?? AnimationFrameSelection.Parse(Settings.Frame, Settings.Frame + Frames - 1).ToArray();
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
