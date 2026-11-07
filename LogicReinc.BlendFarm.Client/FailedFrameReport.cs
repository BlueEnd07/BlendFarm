using System;
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
                return snapshot.Count == 0 ? "" : $"{snapshot.Count} frames failed: {FrameRanges.Format(snapshot.Keys)}";
            }
        }
    }
}

namespace LogicReinc.BlendFarm.Client
{
    public static class FrameRanges
    {
        /// <summary>Compresses frames into ranges ("1-40, 47"), showing at most <paramref name="maxSegments"/> ranges then "… (+N more)".</summary>
        public static string Format(IEnumerable<int> frames, int maxSegments = 6)
        {
            var sorted = new List<int>(new SortedSet<int>(frames));
            var segments = new List<(int Start, int End)>();
            foreach (int frame in sorted)
            {
                if (segments.Count > 0 && segments[segments.Count - 1].End != int.MaxValue && segments[segments.Count - 1].End + 1 == frame)
                    segments[segments.Count - 1] = (segments[segments.Count - 1].Start, frame);
                else
                    segments.Add((frame, frame));
            }
            var parts = new List<string>();
            int shown = 0;
            for (int i = 0; i < segments.Count && i < Math.Max(1, maxSegments); i++)
            {
                var (start, end) = segments[i];
                parts.Add(start == end ? start.ToString() : start + "-" + end);
                shown += end - start + 1;
            }
            string text = string.Join(", ", parts);
            return shown < sorted.Count ? $"{text} … (+{sorted.Count - shown} more)" : text;
        }
    }
}
