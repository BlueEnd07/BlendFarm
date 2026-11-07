using SkiaSharp;
using System;
using System.IO;

namespace LogicReinc.BlendFarm.Objects
{
    /// <summary>
    /// Creates the thumbnail and preview files of history entries.
    /// </summary>
    public static class RenderHistoryImages
    {
        public const int ThumbnailMaxEdge = 360;
        public const int PreviewMaxEdge = 1600;

        /// <summary>
        /// Decodes encoded image data and writes a PNG thumbnail and (optionally) a JPEG preview.
        /// </summary>
        /// <returns>False if the data could not be decoded</returns>
        public static bool TryCreate(byte[] imageData, string thumbnailPath, string previewPath, out int width, out int height)
        {
            width = height = 0;
            if (imageData == null || imageData.Length == 0)
                return false;
            try
            {
                using (SKBitmap source = SKBitmap.Decode(imageData))
                {
                    if (source == null || source.Width <= 0 || source.Height <= 0)
                        return false;
                    width = source.Width;
                    height = source.Height;
                    if (!string.IsNullOrEmpty(thumbnailPath))
                        Write(source, ThumbnailMaxEdge, thumbnailPath, SKEncodedImageFormat.Png, 100);
                    if (!string.IsNullOrEmpty(previewPath))
                        Write(source, PreviewMaxEdge, previewPath, SKEncodedImageFormat.Jpeg, 85);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create history thumbnail: {ex.Message}");
                return false;
            }
        }

        /// <summary>Size that fits within maxEdge on its longest side, never upscaling.</summary>
        public static (int Width, int Height) FitWithin(int width, int height, int maxEdge)
        {
            if (width <= 0 || height <= 0) return (1, 1);
            int longest = Math.Max(width, height);
            if (longest <= maxEdge) return (width, height);
            double f = (double)maxEdge / longest;
            return (Math.Max(1, (int)Math.Round(width * f)), Math.Max(1, (int)Math.Round(height * f)));
        }

        private static void Write(SKBitmap source, int maxEdge, string path, SKEncodedImageFormat format, int quality)
        {
            var (w, h) = FitWithin(source.Width, source.Height, maxEdge);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            SKBitmap scaled = null;
            try
            {
                SKBitmap target = source;
                if (w != source.Width || h != source.Height)
                {
                    scaled = source.Resize(new SKImageInfo(w, h, source.ColorType, source.AlphaType), SKFilterQuality.High);
                    if (scaled != null) target = scaled;
                }
                using (SKImage image = SKImage.FromBitmap(target))
                using (SKData data = image.Encode(format, quality))
                using (FileStream stream = File.Create(path))
                    data.SaveTo(stream);
            }
            finally
            {
                scaled?.Dispose();
            }
        }
    }
}
