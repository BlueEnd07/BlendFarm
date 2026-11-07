using LogicReinc.BlendFarm.Client;
using System;
using System.Collections.Generic;

namespace LogicReinc.BlendFarm.Objects
{
    public partial class OpenBlenderProject
    {
        private int _frameStep = 1;
        private string _frameList = "";
        private bool _encodeMp4;
        private int _encodingFPS;
        public int FrameStep
        {
            get => _frameStep;
            set { _frameStep = value; TriggerPropertyChange(nameof(FrameStep), nameof(FrameSelectionError)); }
        }
        public string FrameList
        {
            get => _frameList;
            set { _frameList = value; TriggerPropertyChange(nameof(FrameList), nameof(FrameSelectionError)); }
        }
        public bool EncodeMp4
        {
            get => _encodeMp4;
            set { _encodeMp4 = value; TriggerPropertyChange(nameof(EncodeMp4)); }
        }
        public int EncodingFPS
        {
            get => _encodingFPS > 0 ? _encodingFPS : (FPS > 0 ? FPS : 24);
            set { _encodingFPS = value; TriggerPropertyChange(nameof(EncodingFPS)); }
        }
        public string FrameSelectionError
        {
            get
            {
                try { GetAnimationFrames(); return ""; }
                catch (FormatException ex) { return ex.Message; }
            }
        }
        public IReadOnlyList<int> GetAnimationFrames() => AnimationFrameSelection.Parse(FrameStart, FrameEnd, FrameStep, FrameList);
    }
}
