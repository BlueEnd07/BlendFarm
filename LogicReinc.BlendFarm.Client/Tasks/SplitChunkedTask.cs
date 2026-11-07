using LogicReinc.BlendFarm.Client.ImageTypes;
using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SkiaSharp;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Client.Tasks
{
    public class SplitChunkedTask : RenderTask, IImageTask
    {
        public SKBitmap FinalImage { get; private set; }
        public byte[] FinalImageData { get; private set; }

        public SplitChunkedTask(List<RenderNode> nodes, string session, string version, long fileId, RenderManagerSettings settings = null) : base(nodes, session, version, fileId, settings)
        {
        }

        protected override async Task<bool> Execute()
        {
            SKBitmap result = TileCompositor.CreateResult(Settings.OutputWidth, Settings.OutputHeight);
            SKCanvas canvas = new SKCanvas(result);
            byte[] wholeFrameData = null;
            bool succeeded = false;
            try
            {

                Dictionary<RenderNode, decimal> shares = GetRelativePerformance(_usedNodes);

                List<RenderSubTask> tasks = GetChunkedSubTasks();

                ConcurrentQueue<RenderSubTask> queue = GetTaskQueueInOrder(tasks, Settings.Order);
                ConcurrentDictionary<RenderSubTask, bool> received = new ConcurrentDictionary<RenderSubTask, bool>();

                Dictionary<RenderNode, List<RenderSubTask>> assignment = _usedNodes.ToDictionary(x => x, y => new List<RenderSubTask>());

                //Divide Tasks
                //Assume every core has the same performance..
                while (queue.Count > 0)
                {
                    RenderSubTask nextTask = null;
                    queue.TryDequeue(out nextTask); //Single threaded, always true

                    RenderNode nextNode = _usedNodes.OrderBy(node =>
                    {
                        int nrTiles = assignment[node].Count;
                        return nrTiles * (1 / shares[node]);
                    }).FirstOrDefault();
                    assignment[nextNode].Add(nextTask);
                }

                //Run tasks over all rendernodes
                await Task.Run(() =>
                {
                    ForceParallel(_usedNodes, (node) =>
                    {
                        List<RenderSubTask> nodeTasks = assignment[node];
                        if (nodeTasks.Count == 0)
                            return;

                        try
                        {
                            SubTaskBatchResult resp = ExecuteSubTasks(node, (rsbt, rbr) =>
                            {
                                ProcessTile(rsbt, rbr.Data, ref canvas, ref result);
                                if (tasks.Count == 1 && IsWholeFrame(rsbt))
                                    wholeFrameData = rbr.Data;

                                received[rsbt] = true;
                                ChangeProgress((double)received.Count / tasks.Count);
                            }, assignment[node].ToArray());
                            if (resp?.Exception != null)
                                node.UpdateException(resp.Exception.Message);
                        }
                        catch (TaskCanceledException ex)
                        {
                            node.UpdateException("Cancelled");
                            return;
                        }
                        catch (AggregateException ex)
                        {
                            node.UpdateException(string.Join(", ", ex.InnerExceptions.Select(x => x.Message)));
                        }
                        catch (Exception ex)
                        {
                            node.UpdateException(ex.Message);
                        }
                    });

                    //Tiles of a node that failed or disconnected, hand them to the nodes still available
                    RenderSubTask[] missing = tasks.Where(x => !received.ContainsKey(x)).ToArray();
                    RenderNode[] available = _usedNodes.Where(x => x.Connected).ToArray();
                    if (missing.Length > 0 && available.Length > 0 && !Cancelled)
                    {
                        HandleQueue(available, missing, (rsbt, tresult) =>
                        {
                            ProcessTile(rsbt, tresult, ref canvas, ref result);
                            if (tasks.Count == 1 && IsWholeFrame(rsbt))
                                wholeFrameData = tresult.Image;
                            received[rsbt] = true;
                            ChangeProgress((double)received.Count / tasks.Count);
                        }, (node, rsbt, ex) => { });
                    }
                });

                if (!Cancelled && received.Count < tasks.Count)
                    throw new Exception($"Not all tiles rendered ({received.Count}/{tasks.Count})");
                succeeded = true;
            }
            finally
            {
                //A failed render hands out no image, release it right away
                ReleaseResult(ref canvas, result, !succeeded);
            }
            FinalImage = result;
            FinalImageData = wholeFrameData;

            return true;
        }
    }
}
