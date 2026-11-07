using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LogicReinc.BlendFarm.Client
{
    public static class AnimationFrameSelection
    {
        public const int MaximumFrames = 1000000;
        public static IReadOnlyList<int> Parse(int start, int end, int step = 1, string frames = null)
        {
            SortedSet<int> selected = new SortedSet<int>();
            if (!string.IsNullOrWhiteSpace(frames))
            {
                foreach (string part in frames.Split(','))
                {
                    string[] bounds = part.Trim().Split('-');
                    if (bounds.Length > 2 || bounds.Any(string.IsNullOrWhiteSpace))
                        throw new FormatException("Use frames or ascending ranges, for example 1-50, 75, 100-120.");
                    int first = ParseFrame(bounds[0]);
                    int last = bounds.Length == 2 ? ParseFrame(bounds[1]) : first;
                    AddRange(selected, first, last, 1);
                }
            }
            else
            {
                if (step < 1) throw new FormatException("Frame step must be at least 1.");
                AddRange(selected, start, end, step);
            }
            return selected.ToArray();
        }
        private static int ParseFrame(string text)
        {
            if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int frame))
                throw new FormatException("Frame numbers must be non-negative integers.");
            return frame;
        }
        private static void AddRange(SortedSet<int> selected, int first, int last, int step)
        {
            if (first < 0 || last < first) throw new FormatException("Frame ranges must be non-negative and ascending.");
            if (((long)last - first) / step + 1 > MaximumFrames) throw new FormatException("Select no more than 1,000,000 frames.");
            for (long frame = first; frame <= last; frame += step)
            {
                selected.Add((int)frame);
                if (selected.Count > MaximumFrames) throw new FormatException("Select no more than 1,000,000 frames.");
            }
        }
    }
}
