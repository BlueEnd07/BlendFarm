using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Client.ImageTypes
{
    public static class ImageConverter
    {
        public static DefaultImageConverter Default { get; } = new DefaultImageConverter();

        /// <summary>
        /// Decodes render output of the given Blender format, null if the format can't be decoded
        /// </summary>
        public static SKBitmap Convert(byte[] bytes, string format)
        {
            if (bytes == null)
                return null;
            using (MemoryStream str = new MemoryStream(bytes))
                return Convert(str, format);
        }
        public static SKBitmap Convert(Stream str, string format)
        {
            format = (format ?? "").ToUpper().Trim();
            switch (format)
            {
                case "":
                case "BMP":
                case "PNG":
                case "JPEG":
                case "TIFF": //Not decodable by Skia, results in null (no preview)
                    return Default.FromStream(str);
                default:
                    return null;
            }
        }
    }

    public interface IImageConverter
    {
        SKBitmap FromStream(Stream str);
    }
}
