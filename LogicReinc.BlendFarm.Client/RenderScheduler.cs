using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Client
{
    /// <summary>
    /// Pull-based scheduler shared by every render strategy.
    /// Every node runs an async worker that takes the next subtask (or a batch of them) from a single queue,
    /// renders it and hands the result to the RenderTask, so fast nodes simply take more work.
    /// Failed subtasks go back to the queue, nodes that keep failing stop taking work, and once the queue is empty
    /// idle nodes may duplicate the in-flight subtask expected to take longest (the first result wins).
    /// </summary>
    internal sealed class RenderScheduler
    {
        /// <summary>
        /// How long an idle node waits before reconsidering duplicating work it skipped because it wouldn't finish sooner
        /// </summary>
        internal const int IDLE_RECHECK_MS = 1000;

        private readonly RenderTask _owner;
        private readonly RenderNode[] _nodes;
        private readonly bool _useBatches;
        private readonly bool _duplicateSlowTasks;

        private readonly object _lock = new object();
        private readonly LinkedList<RenderSubTask> _queue;
        private readonly Dictionary<RenderSubTask, List<Lease>> _running = new Dictionary<RenderSubTask, List<Lease>>();
        private readonly HashSet<RenderNode> _activeNodes;
        private readonly Dictionary<RenderSubTask, HashSet<RenderNode>> _failedOn = new Dictionary<RenderSubTask, HashSet<RenderNode>>();
        private readonly List<Exception> _errors = new List<Exception>();
        private TaskCompletionSource<bool> _changed = NewSignal();
        private int _remaining;
        private int _done;
        private double _doneValue;
        private readonly double _totalValue;

        public IReadOnlyList<RenderSubTask> SubTasks { get; }
        public int Total => SubTasks.Count;
        public int Completed { get { lock (_lock) return _done; } }
        public bool AllDone { get { lock (_lock) return _done == SubTasks.Count; } }
        public List<Exception> Errors { get { lock (_lock) return _errors.ToList(); } }

        public RenderScheduler(RenderTask owner, IReadOnlyList<RenderSubTask> subtasks, IEnumerable<RenderNode> nodes, bool useBatches, bool duplicateSlowTasks)
        {
            _owner = owner;
            SubTasks = subtasks;
            _nodes = nodes.Where(node => node.IsEnabled && node.Connected).ToArray();
            _useBatches = useBatches;
            _duplicateSlowTasks = duplicateSlowTasks;
            _queue = new LinkedList<RenderSubTask>(subtasks);
            _activeNodes = new HashSet<RenderNode>(_nodes);
            _remaining = subtasks.Count;
            _totalValue = subtasks.Sum(x => x.Value);
            foreach (RenderSubTask task in subtasks)
            {
                task.State = SubTaskState.Queued;
                task.Attempts = 0;
            }
        }

        /// <summary>
        /// Runs until every subtask is done or given up on, every node stopped taking work, or the token is cancelled
        /// </summary>
        public async Task Run(CancellationToken token)
        {
            if (Total == 0)
            {
                _owner.ReportProgress(1);
                return;
            }
            if (_nodes.Length == 0)
                throw new InvalidOperationException(NodeConnectionPolicy.NoEnabledNodes);
            await Task.WhenAll(_nodes.Select(node => Task.Run(() => RunWorker(node, token))));
        }

        private async Task RunWorker(RenderNode node, CancellationToken token)
        {
            int consecutiveFailures = 0;
            try
            {
                while (!token.IsCancellationRequested && node.IsEnabled && node.Connected)
                {
                    Lease lease;
                    Task signal;
                    bool deferred;
                    lock (_lock)
                    {
                        if (_remaining <= 0)
                            break;
                        signal = _changed.Task;
                        lease = TryTake(node, out deferred);
                    }
                    if (lease == null)
                    {
                        //Nothing to do right now, wait until the queue or the running work changes
                        await WaitForChange(signal, IDLE_RECHECK_MS, token).ConfigureAwait(false);
                        continue;
                    }

                    LeaseOutcome outcome = await RunLease(lease, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested)
                        break;

                    if (outcome == LeaseOutcome.Succeeded)
                        consecutiveFailures = 0;
                    else if (outcome == LeaseOutcome.Failed)
                        consecutiveFailures++;

                    //Stop using a node that disconnected or keeps failing, the others pick up its work
                    if (!node.Connected)
                        break;
                    if (consecutiveFailures >= RenderTask.MAX_NODE_FAILURES)
                    {
                        node.UpdateException($"{node.Exception} (stopped after {consecutiveFailures} failures)");
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                lock (_lock)
                {
                    _activeNodes.Remove(node);
                    Signal();
                }
                node.UpdateActivity("");
            }
        }

        /// <summary>
        /// Takes work for node from the queue, or a subtask to duplicate once the queue is empty. Called under _lock.
        /// </summary>
        private Lease TryTake(RenderNode node, out bool deferred)
        {
            deferred = false;
            List<RenderSubTask> taken = new List<RenderSubTask>();
            bool duplicate = false;
            if (_queue.Count > 0)
            {
                int count = _useBatches ? GetBatchSize(node) : 1;
                LinkedListNode<RenderSubTask> item = _queue.First;
                while (taken.Count < count && item != null)
                {
                    LinkedListNode<RenderSubTask> next = item.Next;
                    if (!ShouldLeaveForOthers(item.Value, node))
                    {
                        taken.Add(item.Value);
                        _queue.Remove(item);
                    }
                    item = next;
                }
                //Everything queued already failed on this node, wait for the other nodes to take it
                if (taken.Count == 0)
                    return null;
            }
            else if (_duplicateSlowTasks)
            {
                RenderSubTask toDuplicate = FindTaskToDuplicate(node, out deferred);
                if (toDuplicate == null)
                    return null;
                taken.Add(toDuplicate);
                duplicate = true;
            }
            else
                return null;

            Lease lease = new Lease()
            {
                Node = node,
                Tasks = taken.ToArray(),
                Duplicate = duplicate,
                //Single renders use the subtask id as request id (the node's current task), batches get their own
                RequestID = _useBatches ? Guid.NewGuid().ToString() : taken[0].ID,
                Started = DateTime.UtcNow,
                ExpectedMs = node.EstimateRenderMs(taken.Sum(x => x.Pixels))
            };
            foreach (RenderSubTask task in lease.Tasks)
            {
                if (!_running.TryGetValue(task, out List<Lease> runners))
                    _running[task] = runners = new List<Lease>();
                runners.Add(lease);
                task.State = SubTaskState.Running;
            }
            return lease;
        }

        /// <summary>
        /// True if node already failed this subtask and another active node hasn't, so a retry goes elsewhere
        /// </summary>
        private bool ShouldLeaveForOthers(RenderSubTask task, RenderNode node)
        {
            if (!_failedOn.TryGetValue(task, out HashSet<RenderNode> failedOn) || !failedOn.Contains(node))
                return false;
            return _activeNodes.Any(x => x != node && !failedOn.Contains(x));
        }

        /// <summary>
        /// Guided self-scheduling: a node takes a share of the queue proportional to its speed, halved,
        /// so early batches are big (one Blender start-up for many tiles) and they shrink towards the end.
        /// </summary>
        private int GetBatchSize(RenderNode node)
        {
            List<RenderNode> active = _activeNodes.ToList();
            if (!active.Contains(node))
                active.Add(node);
            Dictionary<RenderNode, decimal> shares = _owner.GetRelativePerformance(active);
            double share = (shares.TryGetValue(node, out decimal s) && s > 0) ? (double)s : 1.0 / active.Count;
            int size = (int)Math.Ceiling(_queue.Count * share / RenderTask.BATCH_GUIDED_DIVISOR);
            return Math.Max(1, Math.Min(_queue.Count, size));
        }

        /// <summary>
        /// Picks the in-flight subtask with the longest expected remaining time that only one other node works on.
        /// A subtask is skipped (deferred) if node is known to be too slow to beat the copy that is on schedule.
        /// </summary>
        private RenderSubTask FindTaskToDuplicate(RenderNode node, out bool deferred)
        {
            deferred = false;
            DateTime now = DateTime.UtcNow;
            RenderSubTask best = null;
            double bestRemaining = double.MinValue;
            foreach (KeyValuePair<RenderSubTask, List<Lease>> pair in _running)
            {
                RenderSubTask task = pair.Key;
                if (task.State != SubTaskState.Running || pair.Value.Count != 1)
                    continue;
                Lease other = pair.Value[0];
                if (other.Node == node)
                    continue;

                double elapsed = (now - other.Started).TotalMilliseconds;
                bool onSchedule = other.ExpectedMs.HasValue && elapsed < other.ExpectedMs.Value;
                //Unknown or overdue: rank by age, like the oldest-first rule this replaces
                double remaining = onSchedule ? other.ExpectedMs.Value - elapsed : elapsed;
                if (onSchedule)
                {
                    double? mine = node.EstimateRenderMs(task.Pixels);
                    if (mine.HasValue && mine.Value >= remaining)
                    {
                        deferred = true;
                        continue;
                    }
                }
                if (remaining > bestRemaining)
                {
                    best = task;
                    bestRemaining = remaining;
                }
            }
            return best;
        }

        private async Task<LeaseOutcome> RunLease(Lease lease, CancellationToken token)
        {
            RenderNode node = lease.Node;
            Exception error = null;
            try
            {
                foreach (RenderSubTask task in lease.Tasks)
                    _owner.NotifySubTaskStarted(node, task);
                node.UpdateActivity(GetActivityLabel(lease), 0);

                token.ThrowIfCancellationRequested();
                if (_useBatches)
                    await RunBatch(lease, token).ConfigureAwait(false);
                else
                    await RunSingle(lease, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            return Finish(lease, error, token);
        }

        private async Task RunSingle(Lease lease, CancellationToken token)
        {
            RenderNode node = lease.Node;
            RenderSubTask task = lease.Tasks[0];
            RenderRequest req = task.GetRenderRequest();
            req.Settings.RenderType = node.RenderType;

            Stopwatch time = Stopwatch.StartNew();
            //The node marks the request as its current task before the call yields, so a cancel right after finds it
            Task<RenderResponse> call = node.Render(req);
            RenderResponse resp;
            using (token.Register(() => _ = SafeCancel(node, lease.RequestID)))
                resp = await call.ConfigureAwait(false);
            time.Stop();

            if (resp == null)
                throw new Exception("Render fail: (null)");
            if (!resp.Success)
                throw new Exception("Render fail: " + resp.Message);
            if (resp.Data == null || resp.Data.Length == 0)
                throw new Exception("Unknown Remote Exception");

            node.UpdatePerformance(task.Pixels, time.ElapsedMilliseconds, true);
            Deliver(lease, task, resp.Data, (int)time.ElapsedMilliseconds);
        }

        private async Task RunBatch(Lease lease, CancellationToken token)
        {
            RenderNode node = lease.Node;
            RenderSubTask[] tasks = lease.Tasks;
            Dictionary<string, RenderSubTask> byId = tasks.ToDictionary(x => x.ID);

            //Packets are handled on separate threads, so results may be processed after the batch response.
            //Track which subtasks were handled, to wait for stragglers before unsubscribing.
            HashSet<string> handled = new HashSet<string>();
            HashSet<string> expected = null;
            TaskCompletionSource<bool> allHandled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Stopwatch time = new Stopwatch();
            object timingLock = new object();
            long lastResultMs = 0;
            bool first = true;

            Action<RenderNode, RenderBatchResult> onResult = (bnode, result) =>
            {
                if (result == null || result.TaskID == null || !byId.TryGetValue(result.TaskID, out RenderSubTask task))
                    return;
                try
                {
                    long ms, interval;
                    bool wasFirst;
                    lock (timingLock)
                    {
                        ms = time.ElapsedMilliseconds;
                        interval = ms - lastResultMs;
                        lastResultMs = ms;
                        wasFirst = first;
                        first = false;
                    }
                    if (result.Data == null || result.Data.Length == 0)
                        throw new Exception("Render fail: empty result");

                    //Results arrive one by one from a single Blender instance: the gap between them is pure render time,
                    //the time to the first one also holds the start-up of the request
                    if (wasFirst)
                    {
                        double? renderOnly = node.EstimateRenderMs(task.Pixels) - node.StartupCostMs;
                        if (renderOnly.HasValue)
                            node.UpdateStartupCost(ms - renderOnly.Value);
                        node.UpdatePerformance(task.Pixels, ms, true);
                    }
                    else
                        node.UpdatePerformance(task.Pixels, interval, false);

                    Deliver(lease, task, result.Data, (int)(wasFirst ? ms : interval));
                }
                catch (Exception ex)
                {
                    lock (_lock)
                        lease.LastError = ex;
                }
                finally
                {
                    lock (handled)
                    {
                        handled.Add(task.ID);
                        if (expected != null && expected.IsSubsetOf(handled))
                            allHandled.TrySetResult(true);
                    }
                }
            };

            node.OnBatchResult += onResult;
            try
            {
                RenderBatchRequest req = RenderSubTask.GetRenderBatchRequest(lease.RequestID, tasks);
                req.Settings.ForEach(x => x.RenderType = node.RenderType);

                time.Start();
                Task<RenderBatchResponse> call = node.RenderBatch(req);
                RenderBatchResponse resp;
                using (token.Register(() => _ = SafeCancel(node, lease.RequestID)))
                    resp = await call.ConfigureAwait(false);

                if (resp == null)
                    throw new Exception("Render fail: (null)");
                if (!resp.Success)
                    throw new Exception("Render fail: " + resp.Message);

                //The node sends every result before its response, wait (briefly) until all of them have been handled
                lock (handled)
                {
                    IEnumerable<string> ids = tasks.Select(x => x.ID);
                    if (resp.SubTaskIDs != null && resp.SubTaskIDs.Count > 0)
                        ids = ids.Intersect(resp.SubTaskIDs);
                    expected = new HashSet<string>(ids);
                    if (expected.IsSubsetOf(handled))
                        allHandled.TrySetResult(true);
                }
                await Task.WhenAny(allHandled.Task, Task.Delay(RenderTask.RESULT_GRACE_PERIOD_MS, token)).ConfigureAwait(false);
            }
            finally
            {
                node.OnBatchResult -= onResult;
                time.Stop();
            }
        }

        /// <summary>
        /// Hands a rendered subtask to the RenderTask. The first copy of a subtask wins, later ones are ignored.
        /// Throws if the RenderTask fails to handle it (eg. saving failed), the subtask is then retried.
        /// </summary>
        private bool Deliver(Lease lease, RenderSubTask task, byte[] data, int ms)
        {
            lock (_lock)
            {
                //Late results of a request that was already released (and possibly requeued) are ignored too
                if (_owner.Cancelled || lease.Finished || task.State == SubTaskState.Done || task.State == SubTaskState.Failed)
                    return false;
                //Claim it, so a duplicate finishing at the same time is ignored
                task.State = SubTaskState.Done;
            }

            try
            {
                _owner.HandleSubTaskResult(task, data);
            }
            catch
            {
                lock (_lock)
                {
                    task.State = SubTaskState.Running;
                    //The request was released while this result was being handled, nobody else will retry it
                    if (lease.Finished && !_running.ContainsKey(task))
                    {
                        if (RequeueOrGiveUp(task, true))
                            _queue.AddFirst(task);
                        Signal();
                    }
                }
                throw;
            }

            List<Lease> toCancel = new List<Lease>();
            double progress;
            lock (_lock)
            {
                lease.Delivered.Add(task);
                _remaining--;
                _done++;
                _doneValue += task.Value;
                progress = (_done == Total) ? 1 : Math.Min(1, _doneValue / _totalValue);

                //Stop other copies of this subtask, unless their request still has other work to deliver
                if (_running.TryGetValue(task, out List<Lease> runners))
                    foreach (Lease other in runners)
                        if (other != lease && other.Tasks.All(x => x.State == SubTaskState.Done || x.State == SubTaskState.Failed))
                            toCancel.Add(other);
                Signal();
            }

            lease.Node.RecordRenderCompletion(task.Crop ? 0 : 1, task.Crop ? 1 : 0, ms);
            _owner.NotifySubTaskFinished(lease.Node, task);
            _owner.ReportProgress(progress);

            foreach (Lease other in toCancel)
                _ = SafeCancel(other.Node, other.RequestID); //No-op if that node already moved on
            return true;
        }

        /// <summary>
        /// Releases the subtasks of a finished request, requeueing whatever it failed to deliver
        /// </summary>
        private LeaseOutcome Finish(Lease lease, Exception error, CancellationToken token)
        {
            RenderNode node = lease.Node;
            bool cancelled = token.IsCancellationRequested;
            //A disconnect isn't the subtask's fault, it doesn't use up one of its attempts
            bool countAttempt = node.Connected;
            List<RenderSubTask> failed = new List<RenderSubTask>();
            List<RenderSubTask> requeue = new List<RenderSubTask>();
            lock (_lock)
            {
                lease.Finished = true;
                if (error == null)
                    error = lease.LastError;
                foreach (RenderSubTask task in lease.Tasks)
                {
                    List<Lease> runners = _running.TryGetValue(task, out List<Lease> r) ? r : new List<Lease>();
                    runners.Remove(lease);
                    if (runners.Count == 0)
                        _running.Remove(task);

                    if (lease.Delivered.Contains(task) || task.State == SubTaskState.Done || task.State == SubTaskState.Failed)
                        continue;
                    if (cancelled)
                        continue;

                    failed.Add(task);
                    _owner.NotifySubTaskFailed(node, task);
                    if (!_failedOn.TryGetValue(task, out HashSet<RenderNode> failedOn))
                        _failedOn[task] = failedOn = new HashSet<RenderNode>();
                    failedOn.Add(node);
                    //Another node is still rendering a copy, let that one finish instead of requeueing
                    if (runners.Count > 0)
                        continue;

                    if (RequeueOrGiveUp(task, countAttempt))
                        requeue.Add(task);
                }
                //Retries go to the front, so they aren't left until the very end
                for (int i = requeue.Count - 1; i >= 0; i--)
                    _queue.AddFirst(requeue[i]);

                if (failed.Count > 0 && !cancelled)
                {
                    string message = error?.Message ?? "Missing results";
                    _errors.Add(new Exception($"{DescribeSubTask(failed[0])} on {node.Name}: {message}", error));
                }
                Signal();
            }

            if (cancelled)
                return LeaseOutcome.Cancelled;
            if (failed.Count == 0)
                return (lease.Delivered.Count > 0) ? LeaseOutcome.Succeeded : LeaseOutcome.Lost;

            node.UpdateException($"Render fail: {error?.Message ?? "Missing results"}");
            return LeaseOutcome.Failed;
        }

        /// <summary>
        /// Counts a failed attempt, returns true if the subtask should be queued again. Called under _lock.
        /// </summary>
        private bool RequeueOrGiveUp(RenderSubTask task, bool countAttempt)
        {
            if (countAttempt)
                task.Attempts++;
            if (task.Attempts < RenderTask.MAX_SUBTASK_ATTEMPTS)
            {
                task.State = SubTaskState.Queued;
                return true;
            }
            //Give up, the RenderTask reports it as missing
            task.State = SubTaskState.Failed;
            _remaining--;
            return false;
        }

        private async Task SafeCancel(RenderNode node, string requestID)
        {
            try
            {
                await node.CancelRender(_owner.SessionID, requestID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to cancel {requestID} on {node.Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Waits until signal completes, the timeout passes or the token is cancelled (throws then)
        /// </summary>
        private static async Task WaitForChange(Task signal, int timeoutMs, CancellationToken token)
        {
            using (CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task delay = Task.Delay(timeoutMs, stop.Token);
                await Task.WhenAny(signal, delay).ConfigureAwait(false);
                stop.Cancel();
            }
            token.ThrowIfCancellationRequested();
        }

        private void Signal()
        {
            TaskCompletionSource<bool> old = _changed;
            _changed = NewSignal();
            old.TrySetResult(true);
        }
        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static string DescribeSubTask(RenderSubTask task)
        {
            return task.Crop ? $"Tile ({task.X:0.###}-{task.X2:0.###}, {task.Y:0.###}-{task.Y2:0.###}) of frame {task.Frame}" : $"Frame {task.Frame}";
        }

        private static string GetActivityLabel(Lease lease)
        {
            RenderSubTask task = lease.Tasks[0];
            string label;
            if (!task.Crop)
                label = $"Rendering frame {task.Frame}";
            else if (lease.Tasks.Length > 1)
                label = $"Rendering {lease.Tasks.Length} tiles for frame {task.Frame}";
            else
                label = $"Rendering tile for frame {task.Frame}";
            return label + (lease.Duplicate ? " (helping)" : "");
        }

        private enum LeaseOutcome
        {
            /// <summary>Delivered everything it took</summary>
            Succeeded,
            /// <summary>Delivered nothing new, other copies finished first (not a failure)</summary>
            Lost,
            /// <summary>Failed to deliver some of its subtasks</summary>
            Failed,
            /// <summary>The render was cancelled</summary>
            Cancelled
        }

        /// <summary>
        /// A single request to a node: one subtask, or a batch
        /// </summary>
        private class Lease
        {
            public RenderNode Node;
            public RenderSubTask[] Tasks;
            public string RequestID;
            public bool Duplicate;
            public DateTime Started;
            public double? ExpectedMs;
            public HashSet<RenderSubTask> Delivered = new HashSet<RenderSubTask>();
            public Exception LastError;
            public bool Finished;
        }
    }
}
