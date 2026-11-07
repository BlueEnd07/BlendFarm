using LogicReinc.BlendFarm.Shared;
using System.Collections.Generic;

namespace LogicReinc.BlendFarm.Client.Tasks
{
    /// <summary>
    /// Renders the frame as small chunks (Settings.ChunkWidth/Height), one chunk per request
    /// </summary>
    public class ChunkedTask : TiledImageTask
    {
        public ChunkedTask(List<RenderNode> nodes, string session, string version, long fileId, RenderManagerSettings settings = null) : base(nodes, session, version, fileId, settings)
        {
        }

        protected override IReadOnlyList<RenderSubTask> CreateSubTasks()
        {
            return OrderSubTasks(GetChunkedSubTasks(), Settings.Order);
        }
    }
}
