using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Client.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicReinc.BlendFarm.Tests
{
    [TestClass]
    public class QueueFrameToolsTests
    {
        [TestMethod]
        public void FrameListsAreSortedUniqueAndOverrideRangeAndStep()
        {
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 5, 9, 10 }, AnimationFrameSelection.Parse(90, 1, 0, "9-10, 1-3, 5, 2").ToArray());
        }

        [DataTestMethod]
        [DataRow(1, 6, 2, "1,3,5")]
        [DataRow(0, 0, 1, "0")]
        [DataRow(2147483646, 2147483647, 1, "2147483646,2147483647")]
        public void SteppedFramesIncludeOnlyRequestedFrames(int start, int end, int step, string expected)
        {
            Assert.AreEqual(expected, string.Join(",", AnimationFrameSelection.Parse(start, end, step)));
        }

        [DataTestMethod]
        [DataRow("1,,2")]
        [DataRow("3-1")]
        [DataRow("1-2-3")]
        [DataRow("-1")]
        [DataRow("hello")]
        [DataRow("1.5")]
        [DataRow("0-2147483647")]
        [DataRow("2147483648")]
        public void InvalidFrameListsAreRejected(string text)
        {
            Assert.ThrowsException<FormatException>(() => AnimationFrameSelection.Parse(1, 2, 1, text));
        }

        [TestMethod]
        public void InvalidRangeOrStepIsRejected()
        {
            Assert.ThrowsException<FormatException>(() => AnimationFrameSelection.Parse(5, 1));
            Assert.ThrowsException<FormatException>(() => AnimationFrameSelection.Parse(1, 5, 0));
        }

        [TestMethod]
        public void PauseDoesNotInterruptCurrentWorkAndResumeClearsSchedule()
        {
            var state = new QueueRunState();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Assert.IsTrue(state.CanStart(now));
            state.Pause();
            Assert.IsFalse(state.CanStart(now));
            state.Schedule(now.AddHours(1));
            Assert.IsFalse(state.CanStart(now));
            state.Resume();
            Assert.IsTrue(state.CanStart(now));
            Assert.IsNull(state.ScheduledStart);
        }

        [TestMethod]
        public void ScheduledStartIsOneShotAndRestorable()
        {
            DateTimeOffset due = DateTimeOffset.UtcNow.AddMinutes(1);
            var state = new QueueRunState();
            state.Schedule(due);
            var restored = new QueueRunState();
            restored.Schedule(state.ScheduledStart.Value);
            Assert.IsFalse(restored.TakeScheduledStart(due.AddTicks(-1)));
            Assert.IsTrue(restored.TakeScheduledStart(due));
            Assert.IsFalse(restored.TakeScheduledStart(due.AddDays(1)));
        }

        [TestMethod]
        public void PausedScheduleDoesNotStartAndCancelDisarmsIt()
        {
            var state = new QueueRunState();
            state.Schedule(DateTimeOffset.UtcNow.AddMinutes(-1));
            state.Pause();
            Assert.IsFalse(state.TakeScheduledStart(DateTimeOffset.UtcNow));
            state.CancelSchedule();
            Assert.IsNull(state.ScheduledStart);
        }

        [TestMethod]
        public void TodayAndTomorrowUseExplicitLocalDates()
        {
            DateTime today = new DateTime(2026, 11, 7);
            Assert.AreEqual(today.AddHours(9).AddMinutes(30), QueueRunState.ParseLocalStart(today, false, "09:30").DateTime);
            Assert.AreEqual(today.AddDays(1).AddHours(23), QueueRunState.ParseLocalStart(today, true, "23:00").DateTime);
            Assert.ThrowsException<FormatException>(() => QueueRunState.ParseLocalStart(today, false, "25:00"));
        }

        [TestMethod]
        public void EtaUsesRemainingFractionsAndWallClockFrameAverage()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(35), QueueEstimate.Remaining(new[] { 0.5, 3 }, TimeSpan.FromSeconds(100), 10));
            Assert.IsNull(QueueEstimate.Remaining(new[] { 1.0 }, TimeSpan.Zero, 0));
            Assert.AreEqual(TimeSpan.Zero, QueueEstimate.Remaining(Array.Empty<double>(), TimeSpan.Zero, 0));
            Assert.IsNull(QueueEstimate.Remaining(new[] { double.NaN }, TimeSpan.FromSeconds(1), 1));
        }

        [TestMethod]
        public void FailureReportClearsRecoveredAndLateDuplicateFailures()
        {
            var report = new FailedFrameReport();
            report.RecordFailure(47, "node-a");
            report.RecordFailure(12, "node-b");
            report.RecordSuccess(47);
            report.RecordFailure(47, "late-node");
            report.Finish(new[] { 12, 47, 48 }, false);
            CollectionAssert.AreEqual(new[] { 12, 48 }, report.Frames.Keys.ToArray());
            Assert.AreEqual("node-b", report.Frames[12]);
            Assert.IsNull(report.Frames[48]);
            Assert.AreEqual("2 frames failed: 12, 48", report.Summary);
        }

        [TestMethod]
        public void CancellationIsNotReportedAsFailedFrames()
        {
            var report = new FailedFrameReport();
            report.RecordFailure(1, "node");
            report.Finish(new[] { 1, 2 }, true);
            Assert.AreEqual(0, report.Frames.Count);
        }

        [TestMethod]
        public void AnimationReportsOnlySelectedUnskippedMissingFrames()
        {
            var animation = new AnimationTask(new List<RenderNode>(), "test", "test", 1, 1, 99)
            {
                SelectedFrames = new[] { 1, 47, 99 }, SkipFrames = new HashSet<int> { 1 }
            };
            animation.FailedFrames.RecordSuccess(47);
            animation.FinishFailedFrameReport(false);
            CollectionAssert.AreEqual(new[] { 99 }, animation.FailedFrames.Frames.Keys.ToArray());
        }

        [TestMethod]
        public void FfmpegArgumentsAreSeparateAndUseH264Defaults()
        {
            var args = AnimationEncoding.BuildArguments("a path/manifest.txt", "a path/movie.mp4", 24);
            Assert.AreEqual("a path/movie.mp4", args.Last());
            Assert.AreEqual("libx264", args[args.ToList().IndexOf("-c:v") + 1]);
            Assert.AreEqual("yuv420p", args[args.ToList().IndexOf("-pix_fmt") + 1]);
            Assert.AreEqual("18", args[args.ToList().IndexOf("-crf") + 1]);
            Assert.AreEqual("24", args[args.ToList().IndexOf("-r") + 1]);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => AnimationEncoding.BuildArguments("in", "out", 0));
        }

        [TestMethod]
        public void ManifestPreservesSparseFrameNamesAndEscapesQuotes()
        {
            string manifest = AnimationEncoding.BuildManifest(new[] { "frame 0001.png", "director's frame 0047.png" });
            StringAssert.Contains(manifest, "frame 0001.png'");
            StringAssert.Contains(manifest, "director'\\''s frame 0047.png'");
            Assert.ThrowsException<ArgumentException>(() => AnimationEncoding.BuildManifest(new[] { "a\nb" }));
        }
    }
}
