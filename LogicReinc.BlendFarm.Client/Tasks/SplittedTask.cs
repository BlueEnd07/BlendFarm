using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;

namespace LogicReinc.BlendFarm.Client.Tasks
{
    /// <summary>
    /// Renders the frame as strips (vertical strips for SplitHorizontal, horizontal strips for SplitVertical).
    /// There are a few strips per node, so faster nodes simply take more of them.
    /// </summary>
    public class SplittedTask : TiledImageTask
    {
        /// <summary>
        /// Strips generated per node (with more than one node)
        /// </summary>
        public const int SPLIT_TILES_PER_NODE = 3;
        /// <summary>
        /// Strips are never made narrower than this many pixels (before overlap)
        /// </summary>
        public const int MIN_SPLIT_TILE_PIXELS = 64;
        /// <summary>
        /// Overlap added on each side of a strip (0..1 of the frame)
        /// </summary>
        public const decimal SPLIT_OVERLAP = 0.01m;

        private bool _isVertical;

        public SplittedTask(List<RenderNode> nodes, string session, string version, long fileId, RenderManagerSettings settings = null, bool vertical = false) : base(nodes, session, version, fileId, settings)
        {
            _isVertical = vertical;
        }

        protected override IReadOnlyList<RenderSubTask> CreateSubTasks()
        {
            int axisPixels = _isVertical ? Settings.OutputHeight : Settings.OutputWidth;
            int count = GetSplitTileCount(UsedNodes.Count, axisPixels);

            List<RenderSubTask> tasks = new List<RenderSubTask>();
            foreach ((decimal start, decimal end) in GetSplitRanges(count))
            {
                decimal startX = start;
                decimal endX = end;
                if (count > 1)
                {
                    startX = Math.Max(0, startX - SPLIT_OVERLAP);
                    endX = Math.Min(1, endX + SPLIT_OVERLAP);
                }

                if (!_isVertical)
                    tasks.Add(new RenderSubTask(this, startX, endX, 0, 1, Settings.Frame));
                else
                    tasks.Add(new RenderSubTask(this, 0, 1, startX, endX, Settings.Frame));
            }
            return tasks;
        }

        /// <summary>
        /// Number of strips: one for a single node, otherwise a few per node as long as strips stay MIN_SPLIT_TILE_PIXELS wide
        /// </summary>
        internal static int GetSplitTileCount(int nodeCount, int axisPixels)
        {
            if (nodeCount <= 1)
                return 1;
            int maxCount = Math.Max(1, axisPixels / MIN_SPLIT_TILE_PIXELS);
            return Math.Max(1, Math.Min(nodeCount * SPLIT_TILES_PER_NODE, maxCount));
        }

        /// <summary>
        /// Equal ranges that exactly partition 0..1 (before overlap)
        /// </summary>
        internal static List<(decimal start, decimal end)> GetSplitRanges(int count)
        {
            List<(decimal, decimal)> ranges = new List<(decimal, decimal)>();
            for (int i = 0; i < count; i++)
            {
                decimal start = (decimal)i / count;
                decimal end = (i == count - 1) ? 1m : (decimal)(i + 1) / count;
                ranges.Add((start, end));
            }
            return ranges;
        }
    }
}
