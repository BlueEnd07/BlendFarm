using SkiaSharp;
using System;

namespace LogicReinc.BlendFarm.Client.ImageTypes
{
    /// <summary>
    /// Composes rendered tiles into a final image.
    /// Not thread-safe, callers serialise drawing into the same bitmap (see RenderTask.ResultLock).
    /// </summary>
    public static class TileCompositor
    {
        /// <summary>
        /// Creates a fully transparent result bitmap in the same color/alpha type as decoded tiles
        /// </summary>
        public static SKBitmap CreateResult(int width, int height)
        {
            SKBitmap result = new SKBitmap(new SKImageInfo(width, height, DefaultImageConverter.ColorType, DefaultImageConverter.AlphaType));
            result.Erase(SKColors.Transparent);
            return result;
        }

        /// <summary>
        /// Pixel area of a tile given in Blender border coordinates (0-1, Y from the bottom) within the output
        /// </summary>
        public static SKRectI GetTileRect(decimal x, decimal x2, decimal y, decimal y2, int outputWidth, int outputHeight)
        {
            //Edges are truncated individually (like Blender's border), so neighbouring tiles share edges
            //and a tile ending at 1 always reaches the edge of the image
            int left = (int)(x * outputWidth);
            int right = (int)(x2 * outputWidth);
            int bottom = (int)(y * outputHeight);
            int top = (int)(y2 * outputHeight);
            return SKRectI.Create(left, outputHeight - top, right - left, top - bottom);
        }

        /// <summary>
        /// Draws a tile into the given area, replacing what is there (overlapping tile borders don't accumulate alpha)
        /// </summary>
        public static void DrawTile(SKCanvas canvas, SKBitmap tile, SKRectI area)
        {
            if (canvas == null)
                throw new ArgumentNullException(nameof(canvas));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile), "Tile could not be decoded");
            using (SKPaint paint = new SKPaint() { BlendMode = SKBlendMode.Src, FilterQuality = SKFilterQuality.Low })
                canvas.DrawBitmap(tile, (SKRect)area, paint);
        }
    }
}
