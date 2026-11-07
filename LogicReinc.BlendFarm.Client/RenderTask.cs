using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Client.ImageTypes;
using LogicReinc.BlendFarm.Client.Tasks;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using SkiaSharp;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Shared
{
    /// <summary>
    /// Describes a render task for a specific blender version and blend file
    /// </summary>
    public abstract class RenderTask : INotifyPropertyChanged
    {
        /// <summary>
        /// Times a single subtask may fail (on any node) before it is given up on
        /// </summary>
        public const int MAX_SUBTASK_ATTEMPTS = 3;
        /// <summary>
        /// Consecutive failures after which a node stops taking subtasks in this render
        /// </summary>
        public const int MAX_NODE_FAILURES = 3;
        /// <summary>
        /// How long to wait for batch results still being processed after the batch response arrived
        /// </summary>
        public const int RESULT_GRACE_PERIOD_MS = 10000;
        /// <summary>
        /// A node takes (its share of the queue / this) subtasks per batch, so batches shrink towards the end
        /// </summary>
        public const int BATCH_GUIDED_DIVISOR = 2;

        /// <summary>
        /// Used to identify the task
        /// </summary>
        public string ID { get; set; } = Guid.NewGuid().ToString();
        /// <summary>
        /// Used to differentiate between different instances of the program
        /// </summary>
        public string SessionID { get; set; }
        /// <summary>
        /// Version of blender to use
        /// </summary>
        public string Version { get; set; }
        /// <summary>
        /// FileID of the blender file
        /// </summary>
        public long FileID { get; set; }

        /// <summary>
        /// Settings on what to render
        /// </summary>
        public RenderManagerSettings Settings { get; set; }

        public List<RenderNode> Nodes = new List<RenderNode>();

        /// <summary>
        /// Progress of render
        /// </summary>
        public double Progress { get; set; }

        /// <summary>
        /// Event whenever a tile has finished rendering.
        /// The tile bitmap is disposed once the handlers return, copy it if it's needed afterwards.
        /// </summary>
        public event Action<RenderSubTask, SKBitmap> OnTileProcessed;
        /// <summary>
        /// Event whenever the result bitmap is changed.
        /// Tiles are drawn into it from multiple threads, read it while holding ResultLock.
        /// </summary>
        public event Action<RenderSubTask, SKBitmap> OnResultUpdated;

        /// <summary>
        /// Serialises drawing into, and reading from, the composed result bitmap
        /// </summary>
        public object ResultLock { get; } = new object();
        /// <summary>
        /// Event whenever progress is made
        /// </summary>
        public event Action<RenderTask, double> OnProgress;
        /// <summary>
        /// Event whenever a node starts a render subtask
        /// </summary>
        public event Action<RenderNode, RenderSubTask> OnSubTaskStarted;
        /// <summary>
        /// Event whenever a node finishes a render subtask
        /// </summary>
        public event Action<RenderNode, RenderSubTask> OnSubTaskFinished;
        /// <summary>
        /// Event whenever relevant ui properties change
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// If the task has been executed
        /// </summary>
        public bool Consumed { get; set; }
        /// <summary>
        /// If the task has been cancelled
        /// </summary>
        public bool Cancelled { get; set; }

        protected List<RenderNode> _usedNodes = new List<RenderNode>();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        public RenderTask(List<RenderNode> nodes, string session, string version, long fileId, RenderManagerSettings settings = null)
        {
            if (settings == null)
                settings = new RenderManagerSettings();
            Settings = settings;
            Nodes = nodes;
            SessionID = session;
            Version = version;
            FileID = fileId;
        }


        public async Task<bool> Render()
        {
            if (Consumed)
                throw new InvalidOperationException("Already started render..");
            Consumed = true;
            if (Cancelled)
                return false;

            List<RenderNode> pool = Nodes.Where(x => x.Connected).ToList();
            List<RenderNode> validNodes = new List<RenderNode>();

            foreach (RenderNode node in pool)
            {
                node.UpdateException("");
                node.UpdateActivity("");
            }

            await Task.WhenAll(pool.Select(async x =>
            {
                try
                {
                    await CheckNodeReady(x, validNodes);
                }
                catch (Exception ex)
                {
                    //A node that fails its checks (eg. disconnects) is left out, the others still render
                    x.UpdateException($"Skipped: {ex.Message}");
                }
            }));

            if (validNodes.Count == 0)
                throw new InvalidOperationException("No ready nodes available");

            validNodes = GetUniqueRenderEndpoints(validNodes);
            if (validNodes.Count == 0)
                throw new InvalidOperationException("No unique ready nodes available");

            _usedNodes = validNodes;

            foreach (RenderNode useNode in _usedNodes)
                useNode.UpdateException("");

            bool result = await Execute();

            return result;
        }

        /// <summary>
        /// Adds node to validNodes if it has the version and file and isn't busy
        /// </summary>
        private async Task CheckNodeReady(RenderNode x, List<RenderNode> validNodes)
        {
            bool hasVersion = await x.CheckVersion(Version);
            if (!hasVersion)
            {
                x.UpdateException($"Skipped: Blender version {Version} is not available");
                return;
            }

            bool hasFile = await x.CheckSyncFile(SessionID, FileID);
            if (!hasFile)
            {
                x.UpdateException("Skipped: blend file is not synced");
                return;
            }

            bool isBusy = await x.IsBusy();
            if (isBusy)
            {
                x.UpdateException("Skipped: node is already busy");
                return;
            }

            lock (validNodes)
            {
                validNodes.Add(x);
            }
        }

        private static List<RenderNode> GetUniqueRenderEndpoints(List<RenderNode> nodes)
        {
            Dictionary<string, RenderNode> unique = new Dictionary<string, RenderNode>(StringComparer.OrdinalIgnoreCase);
            foreach (RenderNode node in nodes)
            {
                string key = GetRenderEndpointKey(node);
                if (!unique.ContainsKey(key))
                {
                    unique.Add(key, node);
                    continue;
                }

                node.UpdateException($"Skipped duplicate endpoint for {unique[key].Name}");
                node.UpdateActivity($"Skipped duplicate endpoint for {unique[key].Name}");
            }

            return unique.Values.ToList();
        }

        private static string GetRenderEndpointKey(RenderNode node)
        {
            string address = node?.Address ?? "";
            string host = address;
            string port = "";
            int portIndex = address.LastIndexOf(':');
            if (portIndex >= 0 && portIndex < address.Length - 1)
            {
                host = address.Substring(0, portIndex);
                port = address.Substring(portIndex + 1);
            }

            host = NormalizeHost(host);
            return $"{host}:{port}".ToLowerInvariant();
        }

        private static string NormalizeHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return "";

            host = host.Trim().ToLowerInvariant();
            if (host == "localhost" || host == "127.0.0.1" || host == "::1")
                return "localhost";

            return host;
        }


        /// <summary>
        /// Determines device performance based on past renders or default
        /// </summary>
        public Dictionary<RenderNode, decimal> GetRelativePerformance(List<RenderNode> nodes)
        {
            Dictionary<RenderNode, decimal> perfs = new Dictionary<RenderNode, decimal>();
            if (nodes.Count == 0)
                return new Dictionary<RenderNode, decimal>();
            if (Settings.UseAutoPerformance && !nodes.Any(x=>x.PerformanceScorePP <= 0))
            {
                decimal total = nodes.Sum(x => x.PerformanceScorePP);
                foreach (RenderNode node in nodes)
                    perfs.Add(node, node.PerformanceScorePP / total);
            }
            else
            {
                //Unknown core counts (-1) count as a single core
                decimal total = nodes.Sum(x => (x.Performance > 0) ? (decimal)x.Performance : Math.Max(1, x.Cores));
                foreach(RenderNode node in nodes)
                {
                    decimal perf = (node.Performance > 0) ? (decimal)node.Performance : Math.Max(1, node.Cores);
                    perfs.Add(node, perf / total);
                }
            }
            return perfs;
        }


        /// <summary>
        /// Cancel ongoing rendering: stops handing out work and cancels the renders in progress on the nodes
        /// </summary>
        public async Task Cancel()
        {
            Cancelled = true;
            //Off the caller's (UI) thread, cancelling resumes the worker loops
            Task cancelWorkers = Task.Run(() =>
            {
                try
                {
                    _cancellation.Cancel();
                }
                catch (ObjectDisposedException) { }
            });
            if (Consumed)
                await Task.WhenAll(_usedNodes.Select(async x =>
                {
                    await x.CancelRender(SessionID);
                }));
            await cancelWorkers;
        }


        //Strategy
        /// <summary>
        /// Generates the subtasks (tiles or frames) to render, called once the nodes to use are known (UsedNodes)
        /// </summary>
        protected abstract IReadOnlyList<RenderSubTask> CreateSubTasks();
        /// <summary>
        /// Called before the first result, with the subtasks that will be rendered
        /// </summary>
        protected virtual void BeginResults(IReadOnlyList<RenderSubTask> subtasks) { }
        /// <summary>
        /// Combines a rendered subtask into the result, called once per subtask (late duplicates are filtered out).
        /// May be called from multiple threads at once. Throwing fails the attempt, the subtask is then retried.
        /// </summary>
        protected abstract void HandleResult(RenderSubTask task, byte[] data);
        /// <summary>
        /// Called once rendering stopped, succeeded is false if anything is missing or the render was cancelled
        /// </summary>
        protected virtual void EndResults(bool succeeded, bool cancelled) { }
        /// <summary>
        /// Whether nodes render several subtasks per request (one Blender start-up for many tiles)
        /// </summary>
        protected virtual bool UseBatches => false;
        /// <summary>
        /// What the subtasks are, for messages
        /// </summary>
        protected virtual string SubTaskNoun => "tiles";

        /// <summary>
        /// Nodes taking part in the current render (after the readiness checks)
        /// </summary>
        protected IReadOnlyList<RenderNode> UsedNodes => _usedNodes;

        private async Task<bool> Execute()
        {
            if (Cancelled)
                return false;
            ChangeProgress(0);

            IReadOnlyList<RenderSubTask> subtasks = CreateSubTasks();
            RenderScheduler scheduler = new RenderScheduler(this, subtasks, _usedNodes, UseBatches, Settings.DuplicateSlowTasks);
            Scheduler = scheduler;

            BeginResults(subtasks);
            bool complete = false;
            try
            {
                await scheduler.Run(_cancellation.Token);
                complete = scheduler.AllDone;
            }
            finally
            {
                EndResults(complete && !Cancelled, Cancelled);
            }

            if (Cancelled)
                return false;
            if (!complete)
                throw new AggregateException($"Not all {SubTaskNoun} rendered ({scheduler.Completed}/{scheduler.Total})", scheduler.Errors);
            return true;
        }

        /// <summary>
        /// Scheduler of the current render (for diagnostics and tests)
        /// </summary>
        internal RenderScheduler Scheduler { get; private set; }

        internal void HandleSubTaskResult(RenderSubTask task, byte[] data)
        {
            HandleResult(task, data);
        }

        /// <summary>
        /// Reports progress (completed work / total work), never going backwards
        /// </summary>
        internal void ReportProgress(double progress)
        {
            lock (_progressLock)
            {
                if (progress <= Progress)
                    return;
                Progress = progress;
            }
            TriggerPropUpdate(nameof(Progress));
            OnProgress?.Invoke(this, progress);
        }

        /// <summary>
        /// Decodes an encoded tile and processes it (see ProcessTile)
        /// </summary>
        protected void ProcessTile(RenderSubTask task, byte[] encodedTile, ref SKCanvas canvas, ref SKBitmap result, bool dontDraw = false)
        {
            using (SKBitmap img = ImageConverter.Convert(encodedTile, task.Parent.Settings.RenderFormat))
            {
                if (img == null && !Cancelled)
                    throw new InvalidOperationException("Received tile could not be decoded");
                ProcessTile(task, img, ref canvas, ref result, dontDraw);
            }
        }
        /// <summary>
        /// Handles an incoming tile and trigger the events as well as drawing the tile to the result bitmap.
        /// Drawing is serialised with ResultLock, SKBitmap/SKCanvas are not thread-safe.
        /// </summary>
        protected void ProcessTile(RenderSubTask task, SKBitmap part, ref SKCanvas canvas, ref SKBitmap result, bool dontDraw = false)
        {
            if (Cancelled)
                return;

            if (!dontDraw)
            {
                lock (ResultLock)
                {
                    if (result == null)
                    {
                        result = TileCompositor.CreateResult(part.Width, part.Height);
                        canvas = new SKCanvas(result);
                    }
                    SKRectI area = (task.Crop)
                        ? TileCompositor.GetTileRect(task.X, task.X2, task.Y, task.Y2, task.Parent.Settings.OutputWidth, task.Parent.Settings.OutputHeight)
                        : SKRectI.Create(0, 0, task.Parent.Settings.OutputWidth, task.Parent.Settings.OutputHeight);
                    TileCompositor.DrawTile(canvas, part, area);
                    canvas.Flush();
                }
                OnResultUpdated?.Invoke(task, result);
            }
            OnTileProcessed?.Invoke(task, part);
        }

        /// <summary>
        /// True if the subtask renders the entire frame (single tile)
        /// </summary>
        protected static bool IsWholeFrame(RenderSubTask task)
        {
            return task.X <= 0 && task.Y <= 0 && task.X2 >= 1 && task.Y2 >= 1;
        }

        /// <summary>
        /// Disposes the canvas (and the result, unless it is handed out) under ResultLock,
        /// so a preview still reading the result doesn't see it disposed halfway
        /// </summary>
        protected void ReleaseResult(ref SKCanvas canvas, SKBitmap result, bool disposeResult)
        {
            lock (ResultLock)
            {
                canvas?.Dispose();
                canvas = null;
                if (disposeResult)
                    result?.Dispose();
            }
        }

        //Util
        protected void TriggerPropUpdate(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        protected void ChangeProgress(double progress)
        {
            lock (_progressLock)
                Progress = progress;
            TriggerPropUpdate(nameof(Progress));
            OnProgress?.Invoke(this, progress);
        }
        private readonly object _progressLock = new object();

        protected internal void NotifySubTaskStarted(RenderNode node, RenderSubTask task)
        {
            OnSubTaskStarted?.Invoke(node, task);
        }

        protected internal void NotifySubTaskFinished(RenderNode node, RenderSubTask task)
        {
            OnSubTaskFinished?.Invoke(node, task);
        }

        public static RenderTask GetImageRenderTask(List<RenderNode> nodes, string session, string version, long fileId, RenderManagerSettings settings = null)
        {
            RenderTask task = null;
            switch(settings?.Strategy)
            {
                case RenderStrategy.Chunked:
                    task = new ChunkedTask(nodes, session, version, fileId, settings);
                    break;
                case RenderStrategy.SplitChunked:
                    task = new SplitChunkedTask(nodes, session, version, fileId, settings);
                    break;
                default:
                case RenderStrategy.SplitHorizontal:
                case RenderStrategy.SplitVertical:
                    task = new SplittedTask(nodes, session, version, fileId, settings, settings?.Strategy == RenderStrategy.SplitVertical);
                    break;
            }
            return task;
        }

    }

    /// <summary>
    /// Manages subtask results or error
    /// </summary>
    public class SubTaskResult
    {
        public byte[] Image { get; set; }
        public Exception Exception { get; set; }

        public SubTaskResult(Exception ex)
        {
            Exception = ex;
        }
        public SubTaskResult(byte[] image)
        {
            Image = image;
        }
    }

}
