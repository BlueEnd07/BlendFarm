using System.Collections.Generic;

namespace LogicReinc.BlendFarm.Client
{
    public sealed class FailedFrameReport
    {
        private readonly object _gate = new object();
        private readonly Dictionary<int, string> _failed = new Dictionary<int, string>();
        private readonly HashSet<int> _completed = new HashSet<int>();
        public IReadOnlyDictionary<int, string> Frames
        {
            get { lock (_gate) return new SortedDictionary<int, string>(_failed); }
        }
        public void RecordFailure(int frame, string node)
        {
            lock (_gate)
            {
                if (!_completed.Contains(frame)) _failed[frame] = node;
            }
        }
        public void RecordSuccess(int frame)
        {
            lock (_gate) { _completed.Add(frame); _failed.Remove(frame); }
        }
        public void Finish(IEnumerable<int> requestedFrames, bool cancelled)
        {
            lock (_gate)
            {
                if (cancelled) { _failed.Clear(); return; }
                foreach (int frame in requestedFrames)
                    if (!_completed.Contains(frame) && !_failed.ContainsKey(frame)) _failed.Add(frame, null);
            }
        }
        public string Summary
        {
            get
            {
                var snapshot = Frames;
                return snapshot.Count == 0 ? "" : $"{snapshot.Count} frames failed: {string.Join(", ", snapshot.Keys)}";
            }
        }
    }
}
