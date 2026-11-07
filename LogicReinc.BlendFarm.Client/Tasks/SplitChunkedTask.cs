using LogicReinc.BlendFarm.Shared;
using System.Collections.Generic;

namespace LogicReinc.BlendFarm.Client.Tasks
{
    /// <summary>
    /// Renders the frame as chunks (Settings.ChunkWidth/Height), nodes take them in batches rendered by a single Blender instance.
    /// Batches are sized by node speed and shrink towards the end of the render.
    /// </summary>
    public class SplitChunkedTask : TiledImageTask
    {
        public SplitChunkedTask(List<RenderNode> nodes, string session, string version, long fileId, RenderManagerSettings settings = null) : base(nodes, session, version, fileId, settings)
        {
        }

        protected override bool UseBatches => true;

        protected override IReadOnlyList<RenderSubTask> CreateSubTasks()
        {
            return OrderSubTasks(GetChunkedSubTasks(), Settings.Order);
        }
    }
}
