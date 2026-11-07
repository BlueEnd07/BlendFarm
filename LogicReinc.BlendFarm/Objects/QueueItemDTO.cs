using LogicReinc.BlendFarm.Client;

namespace LogicReinc.BlendFarm.Objects
{
    /// <summary>
    /// Serializable snapshot of a queued render, used to persist the render queue
    /// across restarts. QueueItem itself holds live UI/task references and cannot
    /// be serialized directly.
    /// </summary>
    public class QueueItemDTO
    {
        public string BlendFile { get; set; }
        public RenderManagerSettings Settings { get; set; }
        public int Frames { get; set; } = 1;
        public string FrameFormat { get; set; } = "#.png";
        public string SaveTo { get; set; }

        //Enough project association to rebuild an OpenBlenderProject on load.
        public bool UseNetworkedPath { get; set; }
        public string NetworkPathWindows { get; set; }
        public string NetworkPathLinux { get; set; }
        public string NetworkPathMacOS { get; set; }
    }
}
