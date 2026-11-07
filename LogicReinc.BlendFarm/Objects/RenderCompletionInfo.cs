using System;

namespace LogicReinc.BlendFarm.Objects
{
    /// <summary>
    /// Snapshot of a finished (or failed) render, used to drive the completion
    /// toast and the optional webhook notification.
    /// </summary>
    public class RenderCompletionInfo
    {
        public string SceneName { get; set; }
        public string BlendFile { get; set; }
        public bool Success { get; set; }
        public bool IsAnimation { get; set; }
        public int FrameStart { get; set; }
        public int FrameEnd { get; set; }
        public int FrameCount { get; set; }
        public TimeSpan Elapsed { get; set; }
        public int NodeCount { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Samples { get; set; }
        /// <summary>Engine name on success, or the error message on failure.</summary>
        public string EngineOrError { get; set; }
        /// <summary>Folder the output was saved to, if any (offered as "Open folder" in the toast).</summary>
        public string OutputDirectory { get; set; }
    }
}
