using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Client.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Tests
{
    public partial class SchedulerTests
    {
        [TestMethod]
        public async Task Animation_SparseSelection_OnlyRendersUnskippedSelectedFrames()
        {
            var frames = new ConcurrentDictionary<int, int>();
            AnimationTask task = CreateAnimation(new List<RenderNode> { new FakeNode("sparse", 2) }, 1, 48, frames);
            task.SelectedFrames = new[] { 1, 12, 47, 48 };
            task.SkipFrames.Add(12);
            Assert.IsTrue(await WithTimeout(task.Render()));
            CollectionAssert.AreEquivalent(new[] { 1, 47, 48 }, frames.Keys.ToArray());
            Assert.AreEqual(0, task.FailedFrames.Frames.Count);
        }

        [TestMethod]
        public async Task Animation_Failures_ReportMissingFramesAndResponsibleNode()
        {
            var frames = new ConcurrentDictionary<int, int>();
            AnimationTask task = CreateAnimation(new List<RenderNode> { new FakeNode("failed-node", 1) { AlwaysFail = true } }, 12, 48, frames);
            task.SelectedFrames = new[] { 12, 47, 48 };
            await Assert.ThrowsExceptionAsync<AggregateException>(() => WithTimeout(task.Render()));
            CollectionAssert.AreEqual(new[] { 12, 47, 48 }, task.FailedFrames.Frames.Keys.ToArray());
            Assert.IsTrue(task.FailedFrames.Frames.Values.Any(node => node == "failed-node"));
        }

        [TestMethod]
        public async Task Animation_RecoveredNodeFailure_DoesNotReportFailedFrames()
        {
            var frames = new ConcurrentDictionary<int, int>();
            AnimationTask task = CreateAnimation(new List<RenderNode>
            {
                new FakeNode("failed-node", 1) { AlwaysFail = true }, new FakeNode("recovery-node", 4)
            }, 1, 6, frames);
            Assert.IsTrue(await WithTimeout(task.Render()));
            AssertEachFrameOnce(frames, 1, 6);
            Assert.AreEqual(0, task.FailedFrames.Frames.Count);
        }

        [TestMethod]
        public async Task Animation_ReadinessFailure_ReportsAllRequestedFrames()
        {
            var task = new AnimationTask(new List<RenderNode>(), Session, Version, 1, 1, 100)
            {
                SelectedFrames = new[] { 12, 47, 48 }
            };
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => task.Render());
            CollectionAssert.AreEqual(new[] { 12, 47, 48 }, task.FailedFrames.Frames.Keys.ToArray());
        }
    }
}
