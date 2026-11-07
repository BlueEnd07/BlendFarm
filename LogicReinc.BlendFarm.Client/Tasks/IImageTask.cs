using System;
using System.Collections.Generic;
using SkiaSharp;
using System.Text;

namespace LogicReinc.BlendFarm.Client.Tasks
{
    public interface IImageTask
    {
        /// <summary>
        /// Composed result, owned by the task until a caller takes it over (it is not disposed by the task)
        /// </summary>
        SKBitmap FinalImage { get; }
        /// <summary>
        /// Original encoded output of the node if the whole frame was rendered as a single tile, otherwise null.
        /// Allows saving formats Skia can't encode without re-encoding.
        /// </summary>
        byte[] FinalImageData { get; }
    }
}
