using SkiaSharp;
using System;
using System.IO;

namespace LogicReinc.BlendFarm.Client.ImageTypes
{
    /// <summary>
    /// Encodes and saves images in the format matching their file extension
    /// </summary>
    public static class ImageEncoder
    {
        public const int DefaultJpegQuality = 95;
        public const int DefaultWebpQuality = 100;

        /// <summary>
        /// Format Skia can encode for the extension of the path, null if it can't encode it (eg. tga, exr, tif)
        /// </summary>
        public static SKEncodedImageFormat? GetFormatForPath(string path)
        {
            string ext = Path.GetExtension(path ?? "")?.TrimStart('.').ToLowerInvariant();
            switch (ext)
            {
                case "png":
                    return SKEncodedImageFormat.Png;
                case "jpg":
                case "jpeg":
                    return SKEncodedImageFormat.Jpeg;
                case "webp":
                    return SKEncodedImageFormat.Webp;
                case "bmp":
                    return SKEncodedImageFormat.Bmp;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Encodes a bitmap, PNG keeps the alpha channel, JPEG/BMP have none
        /// </summary>
        public static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality = -1)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));
            if (quality < 0)
                quality = format == SKEncodedImageFormat.Jpeg ? DefaultJpegQuality : (format == SKEncodedImageFormat.Webp ? DefaultWebpQuality : 100);

            if (format == SKEncodedImageFormat.Bmp)
                return EncodeBmp(bitmap);

            using (SKPixmap pixmap = bitmap.PeekPixels())
            using (SKData data = pixmap?.Encode(format, quality))
            {
                if (data == null)
                    throw new InvalidOperationException($"Failed to encode image as {format}");
                return data.ToArray();
            }
        }

        /// <summary>
        /// Saves a bitmap in the format matching the file extension.
        /// For extensions Skia can't encode, originalData (the node's own output of a whole frame) is written as is if available,
        /// otherwise PNG data is written.
        /// </summary>
        public static void Save(SKBitmap bitmap, string path, byte[] originalData = null, int quality = -1)
        {
            SKEncodedImageFormat? format = GetFormatForPath(path);
            if (format == null && originalData != null)
            {
                File.WriteAllBytes(path, originalData);
                return;
            }
            File.WriteAllBytes(path, Encode(bitmap, format ?? SKEncodedImageFormat.Png, quality));
        }

        /// <summary>
        /// Skia 2.88 has no BMP encoder, writes a 32-bit BGRA (unpremultiplied) bottom-up BMP
        /// </summary>
        private static byte[] EncodeBmp(SKBitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            int rowBytes = width * 4;
            int pixelBytes = rowBytes * height;
            const int headerSize = 14 + 40;

            byte[] result = new byte[headerSize + pixelBytes];
            using (MemoryStream str = new MemoryStream(result))
            using (BinaryWriter writer = new BinaryWriter(str))
            {
                writer.Write((byte)'B');
                writer.Write((byte)'M');
                writer.Write(result.Length);
                writer.Write(0);
                writer.Write(headerSize);
                writer.Write(40);
                writer.Write(width);
                writer.Write(height);
                writer.Write((short)1);
                writer.Write((short)32);
                writer.Write(0); //BI_RGB
                writer.Write(pixelBytes);
                writer.Write(2835);
                writer.Write(2835);
                writer.Write(0);
                writer.Write(0);
            }

            SKImageInfo info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            byte[] pixels;
            using (SKBitmap unpremul = new SKBitmap(info))
            using (SKPixmap source = bitmap.PeekPixels())
            using (SKPixmap target = unpremul.PeekPixels())
            {
                if (source == null || !source.ReadPixels(target))
                    throw new InvalidOperationException("Failed to read pixels for BMP");
                pixels = unpremul.Bytes;
            }
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(pixels, y * rowBytes, result, headerSize + (height - 1 - y) * rowBytes, rowBytes);
            return result;
        }
    }
}
