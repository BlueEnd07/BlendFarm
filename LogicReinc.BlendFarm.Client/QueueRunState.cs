using System;
using System.Collections.Generic;

namespace LogicReinc.BlendFarm.Client
{
    public sealed class QueueRunState
    {
        private readonly object _gate = new object();
        private bool _paused;
        private DateTimeOffset? _scheduledStart;
        public bool Paused { get { lock (_gate) return _paused; } }
        public DateTimeOffset? ScheduledStart { get { lock (_gate) return _scheduledStart; } }
        public void Pause() { lock (_gate) _paused = true; }
        public void Resume() { lock (_gate) { _paused = false; _scheduledStart = null; } }
        public void Schedule(DateTimeOffset start) { lock (_gate) { _scheduledStart = start; _paused = false; } }
        public void CancelSchedule() { lock (_gate) _scheduledStart = null; }
        public bool CanStart(DateTimeOffset now)
        {
            lock (_gate) return !_paused && (!_scheduledStart.HasValue || now >= _scheduledStart.Value);
        }
        public bool TakeScheduledStart(DateTimeOffset now)
        {
            lock (_gate)
            {
                if (_paused || !_scheduledStart.HasValue || now < _scheduledStart.Value) return false;
                _scheduledStart = null;
                return true;
            }
        }
        public static DateTimeOffset ParseLocalStart(DateTime today, bool tomorrow, string time)
        {
            if (!DateTime.TryParseExact(time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime parsed))
                throw new FormatException("Enter a time as HH:mm, for example 18:30.");
            DateTime local = DateTime.SpecifyKind(today.Date.AddDays(tomorrow ? 1 : 0).Add(parsed.TimeOfDay), DateTimeKind.Unspecified);
            if (TimeZoneInfo.Local.IsInvalidTime(local))
                throw new FormatException("That time does not exist because the clocks change. Choose another time.");
            return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        }
    }

    public static class QueueEstimate
    {
        public static TimeSpan? Remaining(IEnumerable<double> remainingFrames, TimeSpan totalFrameTime, int timedFrames)
        {
            double remaining = 0;
            foreach (double frames in remainingFrames)
            {
                if (double.IsNaN(frames) || double.IsInfinity(frames) || frames < 0) return null;
                remaining += frames;
            }
            if (remaining == 0) return TimeSpan.Zero;
            if (timedFrames <= 0 || totalFrameTime <= TimeSpan.Zero) return null;
            double ticks = totalFrameTime.Ticks / (double)timedFrames * remaining;
            return ticks >= TimeSpan.MaxValue.Ticks || double.IsInfinity(ticks) ? (TimeSpan?)null : TimeSpan.FromTicks((long)ticks);
        }
    }
}
