using SkiaSharp;
using System;
using System.IO;

namespace LogicReinc.BlendFarm.Client.ImageTypes
{
    public class DefaultImageConverter : IImageConverter
    {
        /// <summary>
        /// Color type every decoded image is converted to, so tiles can be composed and shown without per-format handling
        /// </summary>
        public static SKColorType ColorType => SKImageInfo.PlatformColorType;
        /// <summary>
        /// Alpha type of decoded images and composed results.
        /// Premultiplied is what SKCanvas draws in, transparency (film_transparent) is preserved.
        /// </summary>
        public const SKAlphaType AlphaType = SKAlphaType.Premul;

        /// <summary>
        /// Decodes an image Skia supports (PNG, JPEG, BMP, WEBP, GIF..) into an 8-bit premultiplied bitmap.
        /// Returns null if the data can't be decoded (eg. TIFF, TGA, EXR)
        /// </summary>
        public SKBitmap FromStream(Stream str)
        {
            using (SKManagedStream skStream = new SKManagedStream(str))
            using (SKCodec codec = SKCodec.Create(skStream))
            {
                if (codec == null)
                    return null;

                SKImageInfo info = new SKImageInfo(codec.Info.Width, codec.Info.Height, ColorType, AlphaType);
                SKBitmap bitmap = new SKBitmap(info);
                try
                {
                    SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels());
                    if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                        throw new InvalidOperationException($"Failed to decode image ({result})");
                    return bitmap;
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
            }
        }
    }
}
