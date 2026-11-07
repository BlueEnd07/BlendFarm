using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Objects;
using LogicReinc.BlendFarm.Server;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Tests
{
    /// <summary>Regression tests for the queue pause/schedule/node-wait fixes and the small review items.</summary>
    [TestClass]
    public class QueueRobustnessTests
    {
        #region Pause / schedule state

        [TestMethod]
        public void PausedQueueCannotStartSoItsLoopMustExit()
        {
            // The queue loop exits (and releases IsQueueing) whenever CanStart is false.
            var state = new QueueRunState();
            state.Pause();
            Assert.IsFalse(state.CanStart(DateTimeOffset.Now));
            // Render Now / Start-Resume clears the pause so a new loop may start.
            state.Resume();
            Assert.IsTrue(state.CanStart(DateTimeOffset.Now));
        }

        [TestMethod]
        public void PendingScheduleBlocksStartUntilDueThenPauseBlocksAgain()
        {
            var state = new QueueRunState();
            DateTimeOffset due = DateTimeOffset.Now.AddMinutes(10);
            state.Schedule(due);
            Assert.IsFalse(state.CanStart(DateTimeOffset.Now));
            Assert.IsTrue(state.CanStart(due));
            state.Pause();
            Assert.IsFalse(state.CanStart(due));
        }

        [DataTestMethod]
        [DataRow("6:30", 6, 30)]
        [DataRow("06:30", 6, 30)]
        [DataRow(" 6:05 ", 6, 5)]
        [DataRow("18:30", 18, 30)]
        [DataRow("0:00", 0, 0)]
        public void StartTimeAcceptsOptionalLeadingZero(string text, int hour, int minute)
        {
            var parsed = QueueRunState.ParseLocalStart(new DateTime(2026, 11, 7), false, text);
            Assert.AreEqual(new DateTime(2026, 11, 7, hour, minute, 0), parsed.DateTime);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow(null)]
        [DataRow("6")]
        [DataRow("6:3")]
        [DataRow("24:00")]
        [DataRow("abc")]
        public void InvalidStartTimeIsRejected(string text)
            => Assert.ThrowsException<FormatException>(() => QueueRunState.ParseLocalStart(new DateTime(2026, 11, 7), false, text));

        #endregion

        #region Waiting for nodes

        [TestMethod]
        public void UsableNodeStartsImmediately()
            => Assert.AreEqual(NodeWaitDecision.Ready, QueueNodeWait.Decide(1, 0, TimeSpan.Zero));

        [TestMethod]
        public void ConnectingNodesAreWaitedForUpToTwoMinutes()
        {
            Assert.AreEqual(NodeWaitDecision.Wait, QueueNodeWait.Decide(0, 2, TimeSpan.FromSeconds(30)));
            Assert.AreEqual(NodeWaitDecision.Wait, QueueNodeWait.Decide(0, 1, QueueNodeWait.MaxWait - TimeSpan.FromSeconds(1)));
            Assert.AreEqual(NodeWaitDecision.GiveUp, QueueNodeWait.Decide(0, 1, QueueNodeWait.MaxWait));
        }

        [TestMethod]
        public void NoNodesAtAllGivesUpImmediatelyInsteadOfFailingItems()
            => Assert.AreEqual(NodeWaitDecision.GiveUp, QueueNodeWait.Decide(0, 0, TimeSpan.Zero));

        [TestMethod]
        public void OverdueScheduleWaitsForAutomaticNodeSetupButOnlyBriefly()
        {
            TimeSpan early = TimeSpan.FromSeconds(3);
            Assert.IsTrue(QueueNodeWait.ShouldDelayScheduledStart(0, 0, true, early), "setup still running");
            Assert.IsTrue(QueueNodeWait.ShouldDelayScheduledStart(0, 1, false, early), "node connecting");
            Assert.IsFalse(QueueNodeWait.ShouldDelayScheduledStart(1, 0, true, early), "a node is already usable");
            Assert.IsFalse(QueueNodeWait.ShouldDelayScheduledStart(0, 0, false, early), "setup finished, nothing connecting");
            Assert.IsFalse(QueueNodeWait.ShouldDelayScheduledStart(0, 1, true, QueueNodeWait.MaxWait), "bounded");
        }

        #endregion

        #region Failed frame summary

        [TestMethod]
        public void FrameRangesAreCompressed()
        {
            Assert.AreEqual("1-40, 47", FrameRanges.Format(Enumerable.Range(1, 40).Append(47)));
            Assert.AreEqual("5", FrameRanges.Format(new[] { 5 }));
            Assert.AreEqual("12, 48", FrameRanges.Format(new[] { 48, 12 }));
            Assert.AreEqual("1-3, 5", FrameRanges.Format(new[] { 3, 1, 2, 2, 5 }));
            Assert.AreEqual("", FrameRanges.Format(Array.Empty<int>()));
        }

        [TestMethod]
        public void FrameRangeSummaryIsCappedWithRemainingCount()
        {
            // 1-40, then 20 isolated frames: cap at 6 segments
            var frames = Enumerable.Range(1, 40).Concat(Enumerable.Range(0, 20).Select(i => 100 + i * 2)).ToArray();
            string text = FrameRanges.Format(frames);
            StringAssert.StartsWith(text, "1-40, 100, 102, 104, 106, 108");
            StringAssert.EndsWith(text, "(+15 more)");
            Assert.IsFalse(text.Contains("138"));
        }

        [TestMethod]
        public void FailedFrameReportSummaryUsesRanges()
        {
            var report = new FailedFrameReport();
            foreach (int frame in Enumerable.Range(10, 5)) report.RecordFailure(frame, "n");
            Assert.AreEqual("5 frames failed: 10-14", report.Summary);
        }

        [TestMethod]
        public void FailedFrameReportSummaryIsBoundedForHugeFailures()
        {
            var report = new FailedFrameReport();
            report.Finish(Enumerable.Range(0, 5000).Select(i => i * 2), false);
            Assert.IsTrue(report.Summary.Length < 120, report.Summary);
            StringAssert.Contains(report.Summary, "more)");
        }

        #endregion

        #region Port in the unreachable message

        [DataTestMethod]
        [DataRow("192.168.1.5:15000", 15000)]
        [DataRow("192.168.1.5:15001", 15001)]
        [DataRow("host.local:9", 9)]
        [DataRow("host.local", 15000)]
        [DataRow("", 15000)]
        [DataRow(null, 15000)]
        [DataRow("host:notaport", 15000)]
        public void PortIsTakenFromTheNodeAddress(string address, int port)
            => Assert.AreEqual(port, NodeConnectionPolicy.PortOf(address));

        [TestMethod]
        public void UnreachableMessageNamesTheNodesPort()
        {
            var refused = new SocketException((int)SocketError.ConnectionRefused);
            StringAssert.Contains(NodeConnectionPolicy.DescribeFailure(refused, "10.0.0.2:15234"), "TCP 15234");
            StringAssert.Contains(NodeConnectionPolicy.DescribeFailure(new TimeoutException(), "10.0.0.2:16000"), "TCP 16000");
            Assert.AreEqual(NodeConnectionPolicy.Unreachable, NodeConnectionPolicy.DescribeFailure(refused));
        }

        #endregion

        #region Node enabled flag

        [TestMethod]
        public void TogglingEnabledDoesNotCreateSavedClients()
        {
            var settings = new BlendFarmSettings();
            settings.SetNodeEnabled("Local", false);
            settings.SetNodeEnabled("discovered-pc", false);
            Assert.AreEqual(0, settings.PastClients.Count, "Local/discovered nodes must not become saved clients");
            Assert.IsFalse(settings.IsNodeEnabled("Local"));
            Assert.IsFalse(settings.IsNodeEnabled("discovered-pc"));
            Assert.IsTrue(settings.IsNodeEnabled("other"));
            settings.SetNodeEnabled("Local", true);
            Assert.IsTrue(settings.IsNodeEnabled("Local"));
            Assert.AreEqual(1, settings.DisabledNodes.Count);
        }

        [TestMethod]
        public void LegacyDisabledClientsAreStillHonouredAndMigrate()
        {
            var settings = new BlendFarmSettings();
            settings.PastClients["old"] = new BlendFarmSettings.HistoryClient { Name = "old", Address = "a:1", IsEnabled = false };
            Assert.IsFalse(settings.IsNodeEnabled("old"));
            settings.SetNodeEnabled("old", true);
            Assert.IsTrue(settings.IsNodeEnabled("old"));
            Assert.IsTrue(settings.PastClients["old"].IsEnabled);
            settings.SetNodeEnabled("old", false);
            Assert.IsFalse(settings.IsNodeEnabled("old"));
            Assert.IsTrue(settings.PastClients["old"].IsEnabled, "legacy flag is no longer the source of truth");
        }

        [TestMethod]
        public void DisabledNodesSurviveSerialization()
        {
            var settings = new BlendFarmSettings();
            settings.SetNodeEnabled("n1", false);
            var copy = System.Text.Json.JsonSerializer.Deserialize<BlendFarmSettings>(System.Text.Json.JsonSerializer.Serialize(settings));
            Assert.IsFalse(copy.IsNodeEnabled("n1"));
            // an old settings file has no such field
            var old = System.Text.Json.JsonSerializer.Deserialize<BlendFarmSettings>("{}");
            Assert.IsTrue(old.IsNodeEnabled("n1"));
        }

        [TestMethod]
        public void RenamingKeepsTheDisabledFlag()
        {
            var settings = new BlendFarmSettings();
            settings.SetNodeEnabled("before", false);
            settings.RenameNodeFlags("before", "after");
            Assert.IsFalse(settings.IsNodeEnabled("after"));
            Assert.IsTrue(settings.IsNodeEnabled("before"));
        }

        [TestMethod]
        public void LastOutputFoldersPersistPerProject()
        {
            var settings = new BlendFarmSettings();
            settings.LastOutputFolders[@"C:\p\a.blend"] = @"D:\out";
            var copy = System.Text.Json.JsonSerializer.Deserialize<BlendFarmSettings>(System.Text.Json.JsonSerializer.Serialize(settings));
            Assert.AreEqual(@"D:\out", copy.LastOutputFolders[@"C:\p\a.blend"]);
        }

        #endregion

        #region Queue item requeue

        private static QueueItem NewItem(bool animation = false)
            => new QueueItem(null, null, new RenderManagerSettings { Frame = 1 }, null, animation ? 5 : 1) { Animation = animation };

        [TestMethod]
        public void CancelledItemCanBeRequeued()
        {
            var item = NewItem();
            Assert.IsFalse(item.CanRequeue);
            item.Cancelled = true;
            Assert.IsTrue(item.CanRequeue);
            Assert.IsFalse(item.IsQueued);
            Assert.IsTrue(item.ResetToQueued());
            Assert.IsTrue(item.IsQueued);
            Assert.IsTrue(item.Active);
            Assert.IsNull(item.Task);
        }

        [TestMethod]
        public void FailedAndCompletedItemsAreNotRequeuedAsCancelled()
        {
            var failed = NewItem();
            failed.Cancelled = true;
            failed.Exception = "boom";
            Assert.IsFalse(failed.CanRequeue);
            Assert.IsFalse(failed.ResetToQueued());
            var done = NewItem();
            done.FinishedAllFrames = true;
            Assert.IsFalse(done.CanRequeue);
        }

        [TestMethod]
        public void RequeuedAnimationResumesWithoutRerenderingWrittenFrames()
        {
            var item = NewItem(true);
            item.Cancelled = true;
            Assert.IsTrue(item.ResetToQueued());
            Assert.AreEqual(true, item.SkipExistingFrames);
            var still = NewItem();
            still.Cancelled = true;
            still.ResetToQueued();
            Assert.AreNotEqual(true, still.SkipExistingFrames);
        }

        [TestMethod]
        public void PauseCancelPutsTheItemBackInTheQueueOnceItUnwinds()
        {
            var item = NewItem();
            // "Pause and stop now": cancel + requeue once Execute has unwound
            item.Cancelled = true;
            var field = typeof(QueueItem).GetField("_requeueOnCancel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(item, true);
            item.ApplyPendingRequeue();
            Assert.IsTrue(item.IsQueued, "item must not be lost after Pause and stop now");
            // a normal user cancel afterwards stays cancelled
            item.Cancelled = true;
            item.ApplyPendingRequeue();
            Assert.IsTrue(item.Cancelled);
        }

        #endregion

        #region Reconnect notifications

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private sealed class QuietNode : RenderNode
        {
            public override Task<PrepareResponse> PrepareVersion(string version)
            {
                UpdatePreparedStatus(true);
                return Task.FromResult(new PrepareResponse { Success = true });
            }
        }

        [TestMethod]
        public async Task ReconnectLogsOncePerOutageNotPerAttempt()
        {
            int port = FreePort();
            var server = new RenderServer(port, -1, true, IPAddress.Loopback);
            var node = new QuietNode { Name = "outage", Address = $"127.0.0.1:{port}" };
            var phases = new List<ReconnectPhase>();
            int failures = 0;
            node.ReconnectPhaseChanged += (_, phase) => { lock (phases) phases.Add(phase); };
            node.ConnectionFailed += (_, __) => failures++;
            node.ConfigureReconnect(true, () => "v");
            server.Start();
            try
            {
                await Task.Delay(300);
                await node.ConnectAndPrepare("v");
                server.Stop();
                await WaitFor(() => node.IsReconnecting);
                // let at least two failed attempts happen (5s + 10s backoff would be slow; one failed attempt is enough to prove silence)
                await WaitFor(() => node.ReconnectStatus.StartsWith("Reconnecting (attempt 1)") || node.ReconnectStatus.Contains("attempt 2"), 15000);
                await WaitFor(() => node.ReconnectStatus.Contains("attempt 2"), 20000);
                Assert.AreEqual(0, failures, "failed reconnect attempts must not each raise ConnectionFailed");
                lock (phases) CollectionAssert.AreEqual(new[] { ReconnectPhase.Started }, phases);
                server = new RenderServer(port, -1, true, IPAddress.Loopback);
                server.Start();
                await WaitFor(() => node.Connected && node.ReconnectStatus == "", 30000);
                lock (phases) CollectionAssert.AreEqual(new[] { ReconnectPhase.Started, ReconnectPhase.Succeeded }, phases);
            }
            finally { node.Disconnect(); server.Stop(); }
        }

        private static async Task WaitFor(Func<bool> condition, int milliseconds = 10000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
            Assert.IsTrue(condition(), "Condition did not become true before timeout");
        }

        #endregion
    }
}
