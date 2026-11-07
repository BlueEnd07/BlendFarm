using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;

namespace LogicReinc.BlendFarm.Client.Tasks
{
    /// <summary>
    /// Renders a range of full frames, every frame is handed out through OnFrameResult
    /// </summary>
    public class AnimationTask : RenderTask
    {
        public int StartFrame { get; set; }
        public int EndFrame { get; set; }
        /// <summary>
        /// Frames that are already rendered and should not be rendered again (resume)
        /// </summary>
        public HashSet<int> SkipFrames { get; set; } = new HashSet<int>();

        /// <summary>
        /// Called once per rendered frame. Throwing (eg. the frame couldn't be saved) makes the frame render again.
        /// </summary>
        public event Action<RenderSubTask, SubTaskResult> OnFrameResult;

        public AnimationTask(List<RenderNode> nodes, string session, string version, long fileId, int start, int end, RenderManagerSettings settings = null) : base(nodes, session, version, fileId, settings)
        {
            StartFrame = start;
            EndFrame = end;
        }

        protected override string SubTaskNoun => "frames";

        protected override IReadOnlyList<RenderSubTask> CreateSubTasks()
        {
            List<RenderSubTask> tasks = new List<RenderSubTask>();
            for (int i = StartFrame; i <= EndFrame; i++)
                if (!SkipFrames.Contains(i))
                    tasks.Add(new RenderSubTask(this, 0, 1, 0, 1, i));
            return tasks;
        }

        protected override void HandleResult(RenderSubTask task, byte[] data)
        {
            OnFrameResult?.Invoke(task, new SubTaskResult(data));
        }
    }
}
