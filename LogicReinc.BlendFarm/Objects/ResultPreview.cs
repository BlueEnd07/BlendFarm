using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LogicReinc.BlendFarm.Client;
using SkiaSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Objects
{
    /// <summary>
    /// Shows the result of a tiled render while tiles come in.
    /// Coalesces previews (a new one is skipped while the previous hasn't been shown yet),
    /// copies the pixels off the UI thread and only applies the bitmap on the UI thread.
    /// </summary>
    public class ResultPreview
    {
        private readonly Action<Bitmap> _apply;
        private int _pending = 0;
        private volatile bool _closed = false;

        /// <param name="apply">Called on the UI thread with a new preview bitmap, which it takes ownership of</param>
        public ResultPreview(Action<Bitmap> apply)
        {
            _apply = apply;
        }

        /// <summary>
        /// Handler for RenderTask.OnResultUpdated
        /// </summary>
        public void OnResultUpdated(RenderSubTask task, SKBitmap result)
        {
            if (_closed || Interlocked.CompareExchange(ref _pending, 1, 0) != 0)
                return;
            object resultLock = task?.Parent?.ResultLock;
            Task.Run(() =>
            {
                Bitmap preview = null;
                try
                {
                    preview = result.ToAvaloniaBitmap(resultLock);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to create render preview: " + ex.Message);
                }
                if (preview == null)
                {
                    Interlocked.Exchange(ref _pending, 0);
                    return;
                }
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        //The final image is already shown, don't replace it with an older preview
                        if (_closed)
                            preview.Dispose();
                        else
                            _apply(preview);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed to update render preview: " + ex.Message);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _pending, 0);
                    }
                });
            });
        }

        /// <summary>
        /// Stops showing previews, call on the UI thread before applying the final image
        /// </summary>
        public void Close()
        {
            _closed = true;
        }
    }
}
