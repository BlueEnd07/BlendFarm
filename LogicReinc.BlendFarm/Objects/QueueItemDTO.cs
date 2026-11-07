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
        public bool Animation { get; set; }
        public int[] FrameNumbers { get; set; }
        public int FrameStep { get; set; } = 1;
        public string FrameList { get; set; } = "";
        public int ResolutionScale { get; set; } = 100;
        public bool EncodeMp4 { get; set; }
        public int EncodingFPS { get; set; } = 24;
        public bool? SkipExistingFrames { get; set; }
        public System.DateTime? OutputTimestamp { get; set; }
        public int? OutputFrameStart { get; set; }
        public int? OutputFrameEnd { get; set; }

        //Enough project association to rebuild an OpenBlenderProject on load.
        public bool UseNetworkedPath { get; set; }
        public string NetworkPathWindows { get; set; }
        public string NetworkPathLinux { get; set; }
        public string NetworkPathMacOS { get; set; }
    }
}
