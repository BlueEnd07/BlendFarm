using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Client.ImageTypes;
using LogicReinc.BlendFarm.Client.Tasks;
using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Tests
{
    /// <summary>
    /// Drives the render scheduler with fake nodes, no Blender or network needed
    /// </summary>
    [TestClass]
    public partial class SchedulerTests
    {
        [TestMethod]
        public async Task DisabledNode_DoesNotReceiveScheduledFrames()
        {
            var disabled = new FakeNode("disabled", 1) { IsEnabled = false };
            var enabled = new FakeNode("enabled", 2);
            var frames = new ConcurrentDictionary<int, int>();
            var task = CreateAnimation(new List<RenderNode> { disabled, enabled }, 1, 8, frames);
            Assert.IsTrue(await WithTimeout(task.Render()));
            Assert.AreEqual(0, disabled.Requests);
            AssertEachFrameOnce(frames, 1, 8);
        }

        [TestMethod]
        public async Task AllDisabledNodes_FailImmediatelyWithHelpfulMessage()
        {
            var disabled = new FakeNode("disabled", 1) { IsEnabled = false };
            var frames = new ConcurrentDictionary<int, int>();
            var task = CreateAnimation(new List<RenderNode> { disabled }, 1, 8, frames);
            var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => WithTimeout(task.Render()));
            Assert.AreEqual(NodeConnectionPolicy.NoEnabledNodes, error.Message);
            Assert.AreEqual(0, disabled.Requests);
        }

        [TestMethod]
        public async Task NodeDisabledDuringRender_TakesNoFurtherWork()
        {
            var disabled = new FakeNode("disable-during-render", 2);
            var enabled = new FakeNode("enabled", 4);
            var frames = new ConcurrentDictionary<int, int>();
            var task = CreateAnimation(new List<RenderNode> { disabled, enabled }, 1, 20, frames,
                new RenderManagerSettings { OutputWidth = 64, OutputHeight = 32, DuplicateSlowTasks = false });
            task.OnSubTaskStarted += (node, subtask) => { if (node == disabled) disabled.IsEnabled = false; };
            Assert.IsTrue(await WithTimeout(task.Render()));
            Assert.AreEqual(1, disabled.Requests);
            AssertEachFrameOnce(frames, 1, 20);
        }

        [TestMethod]
        public async Task SchedulerRejectsDisabledNodesIndependentlyOfRenderTask()
        {
            var disabled = new FakeNode("disabled", 1) { IsEnabled = false };
            var frames = new ConcurrentDictionary<int, int>();
            var owner = CreateAnimation(new List<RenderNode> { disabled }, 1, 1, frames);
            var scheduler = new RenderScheduler(owner, new[] { new RenderSubTask(owner, 0, 1, 0, 1, 1) }, new[] { disabled }, false, false);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => scheduler.Run(CancellationToken.None));
            Assert.AreEqual(0, disabled.Requests);
        }

        private const string Version = "3.6.0";
        private const string Session = "session";

        /// <summary>
        /// Render node double: renders by waiting (msPerFrame * share of the frame) and returns
        /// PNG tiles whose pixels encode their absolute position, so stitching can be verified.
        /// </summary>
        private class FakeNode : RenderNode
        {
            private static int _count = 0;
            private readonly object _lock = new object();
            private string _current;
            private CancellationTokenSource _cts;
            private int _requests;

            public double MsPerFrame { get; set; }
            public volatile bool IsConnected = true;
            /// <summary>Every request fails</summary>
            public bool AlwaysFail { get; set; }
            /// <summary>Disconnects halfway through this request (1-based), -1 never</summary>
            public int DisconnectOnRequest { get; set; } = -1;
            /// <summary>Keeps rendering when cancelled by task id (result arrives late)</summary>
            public bool IgnoreTaskCancel { get; set; }

            public int Requests => Volatile.Read(ref _requests);
            public ConcurrentBag<string> CancelledTaskIDs { get; } = new ConcurrentBag<string>();
            public ConcurrentBag<string> Delivered { get; } = new ConcurrentBag<string>();

            public FakeNode(string name, double msPerFrame)
            {
                Name = name;
                Address = $"fake{Interlocked.Increment(ref _count)}:15000";
                MsPerFrame = msPerFrame;
                Cores = 4;
            }

            public override bool Connected => IsConnected;
            public override Task<bool> CheckVersion(string version) => Task.FromResult(true);
            public override Task<bool> CheckSyncFile(string sess, long id) => Task.FromResult(true);
            public override Task<bool> IsBusy() => Task.FromResult(false);

            private CancellationTokenSource Begin(string taskID)
            {
                lock (_lock)
                {
                    _current = taskID;
                    _cts = new CancellationTokenSource();
                    return _cts;
                }
            }
            private void End(CancellationTokenSource cts)
            {
                lock (_lock)
                    if (_cts == cts)
                    {
                        _current = null;
                        _cts = null;
                    }
            }

            private async Task RenderOne(RenderPacketModel settings, int request, CancellationToken token)
            {
                if (!IsConnected)
                    throw new InvalidOperationException("Client not connected");
                double share = (double)((settings.X2 - settings.X) * (settings.Y2 - settings.Y));
                int delay = Math.Max(1, (int)(MsPerFrame * share));
                if (request == DisconnectOnRequest)
                {
                    await Task.Delay(delay / 2, token);
                    IsConnected = false;
                    throw new InvalidOperationException("Connection lost");
                }
                await Task.Delay(delay, token);
                if (AlwaysFail)
                    throw new Exception("Blender crashed");
            }

            public override async Task<RenderResponse> Render(RenderRequest req)
            {
                int request = Interlocked.Increment(ref _requests);
                CancellationTokenSource cts = Begin(req.TaskID);
                try
                {
                    try
                    {
                        await RenderOne(req.Settings, request, cts.Token);
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        return new RenderResponse() { Success = false, Message = ex.Message, TaskID = req.TaskID };
                    }
                    Delivered.Add(req.Settings.TaskID);
                    return new RenderResponse() { Success = true, TaskID = req.TaskID, Data = CreateOutput(req.Settings) };
                }
                finally
                {
                    End(cts);
                }
            }

            public override async Task<RenderBatchResponse> RenderBatch(RenderBatchRequest req)
            {
                int request = Interlocked.Increment(ref _requests);
                CancellationTokenSource cts = Begin(req.TaskID);
                try
                {
                    foreach (RenderPacketModel settings in req.Settings)
                    {
                        try
                        {
                            await RenderOne(settings, request, cts.Token);
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            return new RenderBatchResponse() { Success = false, Message = ex.Message, TaskID = req.TaskID };
                        }
                        Delivered.Add(settings.TaskID);
                        RaiseBatchResult(new RenderBatchResult() { Success = true, TaskID = settings.TaskID, Data = CreateOutput(settings) });
                    }
                    return new RenderBatchResponse() { Success = true, TaskID = req.TaskID, SubTaskIDs = req.Settings.Select(x => x.TaskID).ToList() };
                }
                finally
                {
                    End(cts);
                }
            }

            public override Task CancelRender(string sessionID, string taskID)
            {
                lock (_lock)
                {
                    if (taskID == null || _current != taskID)
                        return Task.CompletedTask;
                    CancelledTaskIDs.Add(taskID);
                    if (!IgnoreTaskCancel)
                        _cts?.Cancel();
                }
                return Task.CompletedTask;
            }

            public override Task CancelRender(string sessionID)
            {
                lock (_lock)
                    _cts?.Cancel();
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Color of output pixel (x, y), top-left origin
        /// </summary>
        private static SKColor PixelAt(int x, int y)
        {
            return new SKColor((byte)(x * 7 % 256), (byte)(y * 13 % 256), (byte)((x + y) % 256), 255);
        }

        private static byte[] CreateOutput(RenderPacketModel settings)
        {
            if (!settings.Crop)
                return BitConverter.GetBytes(settings.Frame); //Animation frames, content doesn't matter
            SKRectI rect = TileCompositor.GetTileRect(settings.X, settings.X2, settings.Y, settings.Y2, settings.Width, settings.Height);
            using (SKBitmap tile = TileCompositor.CreateResult(rect.Width, rect.Height))
            {
                for (int x = 0; x < rect.Width; x++)
                    for (int y = 0; y < rect.Height; y++)
                        tile.SetPixel(x, y, PixelAt(rect.Left + x, rect.Top + y));
                return ImageEncoder.Encode(tile, SKEncodedImageFormat.Png);
            }
        }

        private static AnimationTask CreateAnimation(List<RenderNode> nodes, int start, int end, ConcurrentDictionary<int, int> frames, RenderManagerSettings settings = null)
        {
            AnimationTask task = new AnimationTask(nodes, Session, Version, 1, start, end, settings ?? new RenderManagerSettings() { OutputWidth = 64, OutputHeight = 32 });
            task.OnFrameResult += (sub, result) => frames.AddOrUpdate(sub.Frame, 1, (f, c) => c + 1);
            return task;
        }

        private static void AssertEachFrameOnce(ConcurrentDictionary<int, int> frames, int start, int end)
        {
            CollectionAssert.AreEquivalent(Enumerable.Range(start, end - start + 1).ToList(), frames.Keys.ToList());
            foreach (KeyValuePair<int, int> pair in frames)
                Assert.AreEqual(1, pair.Value, $"Frame {pair.Key} was handed out {pair.Value} times");
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, int ms = 15000)
        {
            Task done = await Task.WhenAny(task, Task.Delay(ms));
            Assert.AreSame(task, done, "Render did not finish in time");
            return await task;
        }

        //(a) + (b)
        [TestMethod]
        public async Task Animation_RendersEveryFrameOnce_FastNodeDoesMore()
        {
            FakeNode fast = new FakeNode("fast", 10);
            FakeNode medium = new FakeNode("medium", 30);
            FakeNode slow = new FakeNode("slow", 90);
            ConcurrentDictionary<int, int> frames = new ConcurrentDictionary<int, int>();
            AnimationTask task = CreateAnimation(new List<RenderNode>() { fast, medium, slow }, 1, 60, frames);
            ConcurrentDictionary<RenderNode, int> finishedBy = new ConcurrentDictionary<RenderNode, int>();
            task.OnSubTaskFinished += (node, sub) => finishedBy.AddOrUpdate(node, 1, (n, c) => c + 1);

            Assert.IsTrue(await WithTimeout(task.Render()));

            AssertEachFrameOnce(frames, 1, 60);
            Assert.AreEqual(1, task.Progress);
            Assert.AreEqual(60, finishedBy.Values.Sum());
            int fastCount = finishedBy.GetValueOrDefault(fast);
            int slowCount = finishedBy.GetValueOrDefault(slow);
            Assert.IsTrue(fastCount > slowCount, $"fast {fastCount} vs slow {slowCount}");
            Assert.IsTrue(fastCount > finishedBy.GetValueOrDefault(medium));
            //Speed is measured as a moving average, fast must come out faster
            Assert.IsTrue(fast.PerformanceScorePP > slow.PerformanceScorePP);
        }

        [TestMethod]
        public async Task Animation_SkipsExistingFrames_AndProgressIsMonotonic()
        {
            FakeNode a = new FakeNode("a", 5);
            FakeNode b = new FakeNode("b", 5);
            ConcurrentDictionary<int, int> frames = new ConcurrentDictionary<int, int>();
            AnimationTask task = CreateAnimation(new List<RenderNode>() { a, b }, 1, 20, frames);
            task.SkipFrames = new HashSet<int>() { 2, 3, 10 };
            List<double> progress = new List<double>();
            task.OnProgress += (t, p) => { lock (progress) progress.Add(p); };

            Assert.IsTrue(await WithTimeout(task.Render()));

            CollectionAssert.AreEquivalent(Enumerable.Range(1, 20).Except(new[] { 2, 3, 10 }).ToList(), frames.Keys.ToList());
            Assert.IsTrue(frames.Values.All(x => x == 1));
            lock (progress)
            {
                for (int i = 1; i < progress.Count; i++)
                    Assert.IsTrue(progress[i] >= progress[i - 1], "Progress went backwards");
                Assert.AreEqual(1, progress.Last());
            }
        }

        [TestMethod]
        public async Task Animation_AllFramesSkipped_Completes()
        {
            ConcurrentDictionary<int, int> frames = new ConcurrentDictionary<int, int>();
            AnimationTask task = CreateAnimation(new List<RenderNode>() { new FakeNode("a", 5) }, 1, 3, frames);
            task.SkipFrames = new HashSet<int>() { 1, 2, 3 };
            Assert.IsTrue(await WithTimeout(task.Render()));
            Assert.AreEqual(0, frames.Count);
            Assert.AreEqual(1, task.Progress);
        }

        //(c)
        [TestMethod]
        public async Task FailingNode_IsExcluded_RenderCompletesOnOthers()
        {
            FakeNode good1 = new FakeNode("good1", 20);
            FakeNode good2 = new FakeNode("good2", 20);
            FakeNode bad = new FakeNode("bad", 1) { AlwaysFail = true };
            ConcurrentDictionary<int, int> frames = new ConcurrentDictionary<int, int>();
            AnimationTask task = CreateAnimation(new List<RenderNode>() { bad, good1, good2 }, 1, 30, frames);

            Assert.IsTrue(await WithTimeout(task.Render()));

            AssertEachFrameOnce(frames, 1, 30);
            Assert.AreEqual(RenderTask.MAX_NODE_FAILURES, bad.Requests, "Failing node should stop after MAX_NODE_FAILURES");
            Assert.AreEqual(0, bad.Delivered.Count);
            Assert.IsTrue(task.Scheduler.SubTasks.All(x => x.Attempts <= 1));
            StringAssert.Contains(bad.Exception, "Render fail");
        }

        [TestMethod]
        public async Task SubTaskFailingEverywhere_GivesUp_AndRenderThrows()
        {
            FakeNode a = new FakeNode("a", 1) { AlwaysFail = true };
            FakeNode b = new FakeNode("b", 1) { AlwaysFail = true };
            ConcurrentDictionary<int, int> frames = new ConcurrentDictionary<int, int>();
            AnimationTask task = CreateAnimation(new List<RenderNode>() { a, b }, 1, 5, frames);

            AggregateException ex = await Assert.ThrowsExceptionAsync<AggregateException>(() => WithTimeout(task.Render()));
            StringAssert.Contains(ex.Message, "Not all frames rendered (0/5)");
            Assert.IsTrue(ex.InnerExceptions.Count > 0);
            Assert.AreEqual(RenderTask.MAX_NODE_FAILURES, a.Requests);
            Assert.AreEqual(RenderTask.MAX_NODE_FAILURES, b.Requests);
        }

        [TestMethod]
        public async Task FrameHandlerFailure_RetriesFrame()
        {
            FakeNode a = new FakeNode("a", 5);
            FakeNode b = new FakeNode("b", 5);
            ConcurrentDictionary<int, int> frames = new ConcurrentDictionary<int, int>();
            AnimationTask task = new AnimationTask(new List<RenderNode>() { a, b }, Session, Version, 1, 1, 10, new RenderManagerSettings() { OutputWidth = 64, OutputHeight = 32 });
            int failures = 0;
            task.OnFrameResult += (sub, result) =>
            {
                //Saving frame 4 fails once
                if (sub.Frame == 4 && Interlocked.Increment(ref failures) == 1)
                    throw new System.IO.IOException("Disk full");
                frames.AddOrUpdate(sub.Frame, 1, (f, c) => c + 1);
            };

            Assert.IsTrue(await WithTimeout(task.Render()));
            AssertEachFrameOnce(frames, 1, 10);
            Assert.AreEqual(1, task.Scheduler.SubTasks.Single(x => x.Frame == 4).Attempts);
        }

        //(d)
        [TestMethod]
        public async Task NodeDisconnect_RequeuesItsWork()
        {
            FakeNode stays = new FakeNode("stays", 20);
            FakeNode leaves = new FakeNode("leaves", 20) { DisconnectOnRequest = 3 };
            ConcurrentDictionary<int, int> frames = new ConcurrentDictionary<int, int>();
            AnimationTask task = CreateAnimation(new List<RenderNode>() { stays, leaves }, 1, 20, frames);

            Assert.IsTrue(await WithTimeout(task.Render()));

            AssertEachFrameOnce(frames, 1, 20);
            Assert.AreEqual(3, leaves.Requests, "A disconnected node takes no more work");
            Assert.AreEqual(2, leaves.Delivered.Count);
            //The lost frame isn't counted against its attempts, it wasn't the frame's fault
            Assert.IsTrue(task.Scheduler.SubTasks.All(x => x.Attempts == 0));
        }

        //(e)
        [TestMethod]
        public async Task EndOfRender_DuplicatesSlowestWork_FirstResultWins()
        {
            FakeNode fast = new FakeNode("fast", 10);
            FakeNode slow = new FakeNode("slow", 10000);
            ConcurrentDictionary<int, int> frames = new ConcurrentDictionary<int, int>();
            AnimationTask task = CreateAnimation(new List<RenderNode>() { fast, slow }, 1, 6, frames);

            Stopwatch watch = Stopwatch.StartNew();
            Assert.IsTrue(await WithTimeout(task.Render()));
            watch.Stop();

            AssertEachFrameOnce(frames, 1, 6);
            Assert.IsTrue(watch.ElapsedMilliseconds < 6000, $"Slow node held up the render ({watch.ElapsedMilliseconds}ms)");
            //The slow node's copy was cancelled by its task id
            Assert.AreEqual(1, slow.CancelledTaskIDs.Count);
            RenderSubTask stolen = task.Scheduler.SubTasks.Single(x => x.ID == slow.CancelledTaskIDs.Single());
            Assert.AreEqual(SubTaskState.Done, stolen.State);
            Assert.AreEqual(0, slow.Delivered.Count);
            Assert.AreEqual(6, fast.Delivered.Count);
        }

        [TestMethod]
        public async Task LateDuplicateResult_IsIgnored()
        {
            FakeNode fast = new FakeNode("fast", 10);
            FakeNode slow = new FakeNode("slow", 400) { IgnoreTaskCancel = true };
            ConcurrentDictionary<int, int> frames = new ConcurrentDictionary<int, int>();
            AnimationTask task = CreateAnimation(new List<RenderNode>() { fast, slow }, 1, 4, frames);

            Assert.IsTrue(await WithTimeout(task.Render()));

            //The slow node still delivered its copy after the fast one won, it must not be handed out twice
            Assert.AreEqual(1, slow.Delivered.Count);
            Assert.AreEqual(1, slow.CancelledTaskIDs.Count);
            AssertEachFrameOnce(frames, 1, 4);
            Assert.AreEqual(1, task.Progress);
        }

        [TestMethod]
        public async Task DuplicationDisabled_WaitsForSlowNode()
        {
            FakeNode fast = new FakeNode("fast", 10);
            FakeNode slow = new FakeNode("slow", 300);
            ConcurrentDictionary<int, int> frames = new ConcurrentDictionary<int, int>();
            AnimationTask task = CreateAnimation(new List<RenderNode>() { fast, slow }, 1, 4, frames,
                new RenderManagerSettings() { OutputWidth = 64, OutputHeight = 32, DuplicateSlowTasks = false });

            Assert.IsTrue(await WithTimeout(task.Render()));
            AssertEachFrameOnce(frames, 1, 4);
            Assert.AreEqual(1, slow.Delivered.Count);
            Assert.AreEqual(0, slow.CancelledTaskIDs.Count);
        }

        //(f)
        [TestMethod]
        public async Task Cancel_MidRender_StopsQuickly()
        {
            List<RenderNode> nodes = new List<RenderNode>() { new FakeNode("a", 200), new FakeNode("b", 300), new FakeNode("c", 500) };
            ConcurrentDictionary<int, int> frames = new ConcurrentDictionary<int, int>();
            AnimationTask task = CreateAnimation(nodes, 1, 100, frames);

            Task<bool> render = task.Render();
            await Task.Delay(450);
            Stopwatch watch = Stopwatch.StartNew();
            await task.Cancel();
            bool result = await WithTimeout(render, 5000);
            watch.Stop();

            Assert.IsFalse(result);
            Assert.IsTrue(task.Cancelled);
            Assert.IsTrue(watch.ElapsedMilliseconds < 2000, $"Cancel took {watch.ElapsedMilliseconds}ms");
            Assert.IsTrue(frames.Count < 100);
            int handedOut = frames.Count;
            await Task.Delay(600);
            Assert.AreEqual(handedOut, frames.Count, "Frames still handed out after cancel");
            Assert.IsTrue(nodes.Cast<FakeNode>().Sum(x => x.Requests) < 20);
        }

        [TestMethod]
        public async Task Cancel_BeforeRender_ReturnsFalse()
        {
            ConcurrentDictionary<int, int> frames = new ConcurrentDictionary<int, int>();
            FakeNode node = new FakeNode("a", 10);
            AnimationTask task = CreateAnimation(new List<RenderNode>() { node }, 1, 5, frames);
            await task.Cancel();
            Assert.IsFalse(await WithTimeout(task.Render()));
            Assert.AreEqual(0, node.Requests);
        }

        //(g)
        [TestMethod]
        public void SplitRanges_PartitionTheFrame()
        {
            foreach (int count in new[] { 1, 2, 3, 7, 9, 12 })
            {
                List<(decimal start, decimal end)> ranges = SplittedTask.GetSplitRanges(count);
                Assert.AreEqual(count, ranges.Count);
                Assert.AreEqual(0m, ranges[0].start);
                Assert.AreEqual(1m, ranges[count - 1].end);
                for (int i = 0; i < count; i++)
                {
                    Assert.IsTrue(ranges[i].end > ranges[i].start);
                    if (i > 0)
                        Assert.AreEqual(ranges[i - 1].end, ranges[i].start, "Gap or overlap between strips");
                }
            }

            Assert.AreEqual(1, SplittedTask.GetSplitTileCount(1, 1920));
            Assert.AreEqual(3 * SplittedTask.SPLIT_TILES_PER_NODE, SplittedTask.GetSplitTileCount(3, 1920));
            //Never narrower than the minimum strip size
            Assert.AreEqual(256 / SplittedTask.MIN_SPLIT_TILE_PIXELS, SplittedTask.GetSplitTileCount(8, 256));
        }

        private static void AssertImage(SKBitmap image, int width, int height)
        {
            Assert.IsNotNull(image);
            Assert.AreEqual(width, image.Width);
            Assert.AreEqual(height, image.Height);
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    Assert.AreEqual(PixelAt(x, y), image.GetPixel(x, y), $"Pixel {x},{y}");
        }

        private static async Task<SKBitmap> RenderImage(RenderStrategy strategy, int width, int height, List<RenderNode> nodes, Action<RenderTask> inspect = null)
        {
            RenderManagerSettings settings = new RenderManagerSettings()
            {
                Strategy = strategy,
                OutputWidth = width,
                OutputHeight = height,
                ChunkWidth = 0.25m,
                ChunkHeight = 0.25m,
                RenderFormat = ""
            };
            RenderTask task = RenderTask.GetImageRenderTask(nodes, Session, Version, 1, settings);
            Assert.IsTrue(await WithTimeout(task.Render()));
            Assert.AreEqual(1, task.Progress, 1e-9);
            inspect?.Invoke(task);
            return ((IImageTask)task).FinalImage;
        }

        [DataTestMethod]
        [DataRow(RenderStrategy.SplitHorizontal)]
        [DataRow(RenderStrategy.SplitVertical)]
        public async Task Split_TilesCoverImage_AndStitchCorrectly(RenderStrategy strategy)
        {
            const int width = 640;
            const int height = 360;
            FakeNode fast = new FakeNode("fast", 300);
            FakeNode medium = new FakeNode("medium", 900);
            FakeNode slow = new FakeNode("slow", 2700);
            List<RenderSubTask> subtasks = null;
            using (SKBitmap image = await RenderImage(strategy, width, height, new List<RenderNode>() { fast, medium, slow }, t => subtasks = t.Scheduler.SubTasks.ToList()))
            {
                //More strips than nodes, the fast node takes more of them
                int axisPixels = (strategy == RenderStrategy.SplitVertical) ? height : width;
                Assert.AreEqual(SplittedTask.GetSplitTileCount(3, axisPixels), subtasks.Count);
                Assert.IsTrue(subtasks.Count > 3);
                Assert.IsTrue(fast.Delivered.Count > slow.Delivered.Count, $"fast {fast.Delivered.Count} vs slow {slow.Delivered.Count}");

                //Strips cover every pixel
                bool[,] covered = new bool[width, height];
                foreach (RenderSubTask sub in subtasks)
                {
                    SKRectI rect = TileCompositor.GetTileRect(sub.X, sub.X2, sub.Y, sub.Y2, width, height);
                    for (int x = rect.Left; x < rect.Right; x++)
                        for (int y = rect.Top; y < rect.Bottom; y++)
                            covered[x, y] = true;
                }
                for (int x = 0; x < width; x++)
                    for (int y = 0; y < height; y++)
                        Assert.IsTrue(covered[x, y], $"Pixel {x},{y} not covered");

                AssertImage(image, width, height);
            }
        }

        [TestMethod]
        public async Task Split_SingleNode_RendersWholeFrameOnce()
        {
            FakeNode node = new FakeNode("only", 10);
            RenderManagerSettings settings = new RenderManagerSettings() { Strategy = RenderStrategy.SplitHorizontal, OutputWidth = 64, OutputHeight = 32 };
            RenderTask task = RenderTask.GetImageRenderTask(new List<RenderNode>() { node }, Session, Version, 1, settings);
            Assert.IsTrue(await WithTimeout(task.Render()));
            Assert.AreEqual(1, task.Scheduler.Total);
            //Single whole-frame tile keeps the node's original output
            Assert.IsNotNull(((IImageTask)task).FinalImageData);
            using (SKBitmap image = ((IImageTask)task).FinalImage)
                AssertImage(image, 64, 32);
        }

        [DataTestMethod]
        [DataRow(RenderStrategy.Chunked)]
        [DataRow(RenderStrategy.SplitChunked)]
        public async Task Chunked_StitchesCorrectly(RenderStrategy strategy)
        {
            FakeNode fast = new FakeNode("fast", 40);
            FakeNode slow = new FakeNode("slow", 200);
            using (SKBitmap image = await RenderImage(strategy, 200, 120, new List<RenderNode>() { fast, slow }))
                AssertImage(image, 200, 120);
            Assert.IsTrue(fast.Delivered.Count > 0);
        }

        [TestMethod]
        public async Task SplitChunked_FailingNode_TilesGoToOthers()
        {
            FakeNode good = new FakeNode("good", 40);
            FakeNode bad = new FakeNode("bad", 1) { AlwaysFail = true };
            using (SKBitmap image = await RenderImage(RenderStrategy.SplitChunked, 200, 120, new List<RenderNode>() { bad, good }))
                AssertImage(image, 200, 120);
            Assert.AreEqual(0, bad.Delivered.Count);
            Assert.IsTrue(bad.Requests <= RenderTask.MAX_NODE_FAILURES);
        }

        [TestMethod]
        public async Task FailedImageRender_HandsOutNoImage()
        {
            FakeNode bad = new FakeNode("bad", 1) { AlwaysFail = true };
            RenderManagerSettings settings = new RenderManagerSettings() { Strategy = RenderStrategy.Chunked, OutputWidth = 64, OutputHeight = 32, ChunkWidth = 0.5m, ChunkHeight = 0.5m };
            RenderTask task = RenderTask.GetImageRenderTask(new List<RenderNode>() { bad }, Session, Version, 1, settings);
            AggregateException ex = await Assert.ThrowsExceptionAsync<AggregateException>(() => WithTimeout(task.Render()));
            StringAssert.Contains(ex.Message, "Not all tiles rendered");
            Assert.IsNull(((IImageTask)task).FinalImage);
        }

        [TestMethod]
        public void Performance_IsMovingAverage_WithStartupCost()
        {
            FakeNode node = new FakeNode("n", 1);
            Assert.IsNull(node.EstimateRenderMs(1000));
            node.UpdatePerformance(1000, 100, false);
            Assert.AreEqual(10m, node.PerformanceScorePP);
            node.UpdatePerformance(1000, 50, false);
            //0.3 * 20 + 0.7 * 10
            Assert.AreEqual(13.0, (double)node.PerformanceScorePP, 1e-9);

            node.UpdateStartupCost(200);
            Assert.AreEqual(200, node.StartupCostMs);
            Assert.AreEqual(200 + 1300 / 13.0, node.EstimateRenderMs(1300).Value, 1e-9);
            //A sample that includes start-up has it taken off first: 1300px in 300ms - 200ms start-up = 13 px/ms
            node.UpdatePerformance(1300, 300, true);
            Assert.AreEqual(13.0, (double)node.PerformanceScorePP, 1e-9);
        }
    }
}
