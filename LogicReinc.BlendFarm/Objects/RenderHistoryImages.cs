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
            => TryCreate(imageData, thumbnailPath, previewPath, out width, out height, out _);

        /// <summary>
        /// Same, and reports the preview path actually written: renders with transparency get a PNG preview
        /// (the extension is switched to .png) because JPEG has no alpha and would turn them black.
        /// </summary>
        public static bool TryCreate(byte[] imageData, string thumbnailPath, string previewPath, out int width, out int height, out string writtenPreviewPath)
        {
            width = height = 0;
            writtenPreviewPath = null;
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
                    {
                        bool alpha = HasTransparency(source);
                        writtenPreviewPath = alpha ? Path.ChangeExtension(previewPath, ".png") : previewPath;
                        Write(source, PreviewMaxEdge, writtenPreviewPath, alpha ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, alpha ? 100 : 85);
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create history thumbnail: {ex.Message}");
                return false;
            }
        }

        /// <summary>True if any pixel is not fully opaque (an alpha channel that is 255 everywhere doesn't count).</summary>
        public static bool HasTransparency(SKBitmap bitmap)
        {
            if (bitmap == null || bitmap.AlphaType == SKAlphaType.Opaque)
                return false;
            if (bitmap.ColorType != SKColorType.Bgra8888 && bitmap.ColorType != SKColorType.Rgba8888)
            {
                //Rare formats: ask Skia per pixel (slow but only reached for unusual decodes)
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                        if (bitmap.GetPixel(x, y).Alpha != 255)
                            return true;
                return false;
            }
            //Both 8-bit formats keep alpha in the 4th byte
            ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
            int rowBytes = bitmap.RowBytes;
            for (int y = 0; y < bitmap.Height; y++)
            {
                ReadOnlySpan<byte> row = pixels.Slice(y * rowBytes, bitmap.Width * 4);
                for (int x = 3; x < row.Length; x += 4)
                    if (row[x] != 255)
                        return true;
            }
            return false;
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
