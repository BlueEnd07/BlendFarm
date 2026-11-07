using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LogicReinc.BlendFarm.Client.ImageTypes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm
{
    public static class Statics
    {
        private static object _previewImageLock = new object();
        private static Avalonia.Media.Imaging.Bitmap _noPreviewImage { get; set; }
        public static Avalonia.Media.Imaging.Bitmap NoPreviewImage
        {
            get
            {
                lock (_previewImageLock)
                {
                    if (_noPreviewImage == null)
                    {
                        using (SKBitmap bmp = TileCompositor.CreateResult(700, 200))
                        {
                            using (SKCanvas canvas = new SKCanvas(bmp))
                            using (SKPaint paint = new SKPaint() { Color = SKColors.Gray, TextSize = 21, IsAntialias = true })
                            {
                                canvas.DrawText("Could not generate Preview", 5, 26, paint);
                                canvas.DrawText("(Some Render Formats do not support preview)", 5, 52, paint);
                            }
                            _noPreviewImage = bmp.ToAvaloniaBitmap();
                        }
                    }
                }
                return _noPreviewImage;
            }
        }

        /// <summary>
        /// True for bitmaps shared between projects, which must never be disposed
        /// </summary>
        public static bool IsSharedImage(Bitmap bitmap)
        {
            return bitmap != null && ReferenceEquals(bitmap, _noPreviewImage);
        }

        /// <summary>
        /// Disposes a replaced UI bitmap once the UI has moved on to its replacement.
        /// Background priority runs after the (Normal priority) jobs that swap the image source and after rendering.
        /// </summary>
        public static void DisposeReplacedBitmap(Bitmap old)
        {
            if (old == null || IsSharedImage(old))
                return;
            Dispatcher.UIThread.Post(() => _bitmapGuard.Dispose(old), DispatcherPriority.Background);
        }

        //Background work that encodes a bitmap holds a lease: the disposal waits for it (see DisposalGuard)
        private static readonly LogicReinc.BlendFarm.Objects.DisposalGuard _bitmapGuard = new LogicReinc.BlendFarm.Objects.DisposalGuard(
            dispose => Dispatcher.UIThread.Post(dispose, DispatcherPriority.Background));

        /// <summary>Brush from the application's theme resources (App.axaml / MainStyle.axaml), with a fallback colour for hosts without them (tests).</summary>
        public static Avalonia.Media.IBrush ThemeBrush(string key, string fallbackColor)
        {
            try
            {
                if (Application.Current?.FindResource(key) is Avalonia.Media.IBrush brush)
                    return brush;
            }
            catch { }
            return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(fallbackColor));
        }

        /// <summary>
        /// Keeps a UI bitmap alive for work on another thread (eg. encoding it): replacing it in the project only
        /// postpones the disposal until the lease is disposed. Call on the UI thread while the bitmap is still current.
        /// </summary>
        public static IDisposable LeaseBitmap(Bitmap bitmap) => IsSharedImage(bitmap) ? _bitmapGuard.Acquire(null) : _bitmapGuard.Acquire(bitmap);

        /// <summary>
        /// Saves an image using the format matching its extension (png, jpg, webp, bmp).
        /// Other extensions get originalData (the node's own output of a whole frame) if available, otherwise PNG data.
        /// </summary>
        public static void SaveImage(SKBitmap image, string path, byte[] originalData = null)
        {
            ImageEncoder.Save(image, path, originalData);
        }
        public static void SaveImage(Bitmap bitmap, string path)
        {
            if (ImageEncoder.GetFormatForPath(path) == SKEncodedImageFormat.Png)
            {
                bitmap.Save(path);
                return;
            }
            using (MemoryStream str = new MemoryStream())
            {
                bitmap.Save(str);
                str.Position = 0;
                using (SKBitmap image = ImageConverter.Default.FromStream(str))
                {
                    if (image == null)
                        throw new InvalidOperationException("Failed to read image");
                    ImageEncoder.Save(image, path);
                }
            }
        }

        public static string SanitizePath(string inputPath)
        {
            if (inputPath == null)
                return inputPath;

            //Fix Linux Space escape
            inputPath = inputPath.Replace("\\040", " ");


            return inputPath;
        }

        public static string FormatAnimationFrameFileName(string frameFormat, int frame, int startFrame, int endFrame)
        {
            //Delegates to the shared, unit-tested templating engine (frame-number '#' handling only).
            return Shared.OutputNaming.ApplyFrameHashes(frameFormat, frame, startFrame, endFrame);
        }

        /// <summary>
        /// Copies the pixels of a Skia bitmap into a new Avalonia bitmap (no encoding), can be called off the UI thread.
        /// Returns null if the bitmap was disposed already.
        /// </summary>
        /// <param name="syncLock">Lock to hold while reading, for bitmaps that are drawn into concurrently (RenderTask.ResultLock)</param>
        public static Bitmap ToAvaloniaBitmap(this SKBitmap bitmap, object syncLock = null)
        {
            if (bitmap == null)
                return null;
            if (syncLock == null)
                return CopyToAvaloniaBitmap(bitmap);
            lock (syncLock)
                return CopyToAvaloniaBitmap(bitmap);
        }
        private static Bitmap CopyToAvaloniaBitmap(SKBitmap bitmap)
        {
            if (bitmap.Handle == IntPtr.Zero || bitmap.Width <= 0 || bitmap.Height <= 0)
                return null;

            PixelFormat? format = null;
            if (bitmap.ColorType == SKColorType.Bgra8888)
                format = PixelFormat.Bgra8888;
            else if (bitmap.ColorType == SKColorType.Rgba8888)
                format = PixelFormat.Rgba8888;

            AlphaFormat? alpha = null;
            if (bitmap.AlphaType == SKAlphaType.Premul)
                alpha = AlphaFormat.Premul;
            else if (bitmap.AlphaType == SKAlphaType.Unpremul)
                alpha = AlphaFormat.Unpremul;
            else if (bitmap.AlphaType == SKAlphaType.Opaque)
                alpha = AlphaFormat.Opaque;

            //Avalonia copies the pixels
            if (format != null && alpha != null)
                return new Bitmap(format.Value, alpha.Value, bitmap.GetPixels(), new PixelSize(bitmap.Width, bitmap.Height), new Vector(96, 96), bitmap.RowBytes);

            //Other color types (eg. future 16-bit/float results), convert to 8-bit premultiplied first
            using (SKBitmap converted = new SKBitmap(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul)))
            using (SKPixmap source = bitmap.PeekPixels())
            using (SKPixmap target = converted.PeekPixels())
            {
                if (source == null || !source.ReadPixels(target))
                    throw new InvalidOperationException($"Unsupported image color type {bitmap.ColorType}");
                return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, converted.GetPixels(), new PixelSize(converted.Width, converted.Height), new Vector(96, 96), converted.RowBytes);
            }
        }
    }
}
