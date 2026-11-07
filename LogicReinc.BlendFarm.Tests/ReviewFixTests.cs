using LogicReinc.BlendFarm.Controls;
using LogicReinc.BlendFarm.Objects;
using LogicReinc.BlendFarm.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Tests
{
    /// <summary>Regression tests for the review findings on the preview / history / shortcut work.</summary>
    [TestClass]
    public class ReviewFixTests
    {
        private sealed class Probe : IDisposable
        {
            public int Disposed;
            public bool IsDisposed => Volatile.Read(ref Disposed) != 0;
            public void Dispose() => Interlocked.Increment(ref Disposed);
        }

        // ------------------------------------------------------------ 1: bitmap lease

        [TestMethod]
        public void DisposalGuard_DisposesImmediatelyWithoutLeases()
        {
            var guard = new DisposalGuard();
            var probe = new Probe();
            Assert.IsTrue(guard.Dispose(probe));
            Assert.AreEqual(1, probe.Disposed);
        }

        [TestMethod]
        public void DisposalGuard_PostponesDisposalUntilTheLeaseIsReleased()
        {
            var guard = new DisposalGuard();
            var probe = new Probe();
            IDisposable lease = guard.Acquire(probe);
            Assert.IsFalse(guard.Dispose(probe));
            Assert.IsFalse(probe.IsDisposed, "disposed while a lease was held");
            lease.Dispose();
            Assert.AreEqual(1, probe.Disposed);
            Assert.AreEqual(0, guard.LeaseCount(probe));
        }

        [TestMethod]
        public void DisposalGuard_WaitsForTheLastOfSeveralLeases()
        {
            var guard = new DisposalGuard();
            var probe = new Probe();
            IDisposable a = guard.Acquire(probe), b = guard.Acquire(probe);
            guard.Dispose(probe);
            a.Dispose();
            Assert.IsFalse(probe.IsDisposed);
            b.Dispose();
            Assert.AreEqual(1, probe.Disposed);
        }

        [TestMethod]
        public void DisposalGuard_ReleasingTwiceDoesNotStealAnotherLease()
        {
            var guard = new DisposalGuard();
            var probe = new Probe();
            IDisposable a = guard.Acquire(probe), b = guard.Acquire(probe);
            guard.Dispose(probe);
            a.Dispose();
            a.Dispose();
            Assert.IsFalse(probe.IsDisposed, "a double release dropped the second lease");
            b.Dispose();
            Assert.AreEqual(1, probe.Disposed);
        }

        [TestMethod]
        public void DisposalGuard_LeaseWithoutDisposeRequestNeverDisposes()
        {
            var guard = new DisposalGuard();
            var probe = new Probe();
            guard.Acquire(probe).Dispose();
            Assert.IsFalse(probe.IsDisposed);
        }

        [TestMethod]
        public void DisposalGuard_RunsThePostponedDisposalThroughTheScheduler()
        {
            var queued = new List<Action>();
            var guard = new DisposalGuard(queued.Add);
            var probe = new Probe();
            IDisposable lease = guard.Acquire(probe);
            guard.Dispose(probe);
            lease.Dispose();
            Assert.IsFalse(probe.IsDisposed, "must wait for the scheduler (the UI thread)");
            Assert.AreEqual(1, queued.Count);
            queued[0]();
            Assert.AreEqual(1, probe.Disposed);
        }

        [TestMethod]
        public void DisposalGuard_NullTargetsAreHarmless()
        {
            var guard = new DisposalGuard();
            guard.Acquire(null).Dispose();
            Assert.IsTrue(guard.Dispose(null));
        }

        [TestMethod]
        public void DisposalGuard_NeverDisposesWhileAnotherThreadWorks()
        {
            //The crash scenario: the UI replaces/disposes the image while a worker is still encoding it
            for (int round = 0; round < 50; round++)
            {
                var guard = new DisposalGuard();
                var probe = new Probe();
                IDisposable lease = guard.Acquire(probe);
                bool disposedDuringWork = false;
                var started = new ManualResetEventSlim();
                Task worker = Task.Run(() =>
                {
                    started.Set();
                    for (int i = 0; i < 2000; i++)
                        if (probe.IsDisposed) { disposedDuringWork = true; break; }
                    lease.Dispose();
                });
                started.Wait();
                guard.Dispose(probe);
                worker.Wait();
                Assert.IsFalse(disposedDuringWork);
                Assert.IsTrue(probe.IsDisposed, "disposal must still happen after the work is done");
            }
        }

        // ------------------------------------------------------------ 2: Esc policy

        [TestMethod]
        public void Escape_IsIgnoredWhileTypingInATextBoxOrWithAnOpenDropDown()
        {
            TimeSpan long_ = TimeSpan.FromMinutes(1);
            Assert.AreEqual(EscapeDecision.Ignore, ShortcutCatalog.DecideEscape(true, true, false, long_));
            Assert.AreEqual(EscapeDecision.Ignore, ShortcutCatalog.DecideEscape(true, true, false, TimeSpan.Zero));
            Assert.AreEqual(EscapeDecision.Ignore, ShortcutCatalog.DecideEscape(true, false, true, long_));
        }

        [TestMethod]
        public void Escape_DoesNothingWhenNothingRenders()
        {
            Assert.AreEqual(EscapeDecision.Ignore, ShortcutCatalog.DecideEscape(false, false, false, TimeSpan.FromMinutes(1)));
        }

        [TestMethod]
        public void Escape_CancelsShortRendersAndAsksForLongOnes()
        {
            Assert.AreEqual(EscapeDecision.Cancel, ShortcutCatalog.DecideEscape(true, false, false, TimeSpan.FromSeconds(1)));
            Assert.AreEqual(EscapeDecision.Cancel, ShortcutCatalog.DecideEscape(true, false, false, ShortcutCatalog.CancelConfirmAfter));
            Assert.AreEqual(EscapeDecision.Confirm, ShortcutCatalog.DecideEscape(true, false, false, TimeSpan.FromSeconds(5.1)));
            Assert.AreEqual(EscapeDecision.Confirm, ShortcutCatalog.DecideEscape(true, false, false, TimeSpan.FromMinutes(20)));
        }

        // ------------------------------------------------------------ 5: transparent previews

        private static byte[] PngWithAlpha(int w, int h, byte alpha)
        {
            using (var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul)))
            {
                bmp.Erase(new SKColor(200, 30, 30, alpha));
                using (var image = SKImage.FromBitmap(bmp))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                    return data.ToArray();
            }
        }

        [TestMethod]
        public void HasTransparency_SeesRealAlphaOnly()
        {
            using (var opaque = SKBitmap.Decode(PngWithAlpha(40, 20, 255)))
                Assert.IsFalse(RenderHistoryImages.HasTransparency(opaque), "an alpha channel of 255 is not transparency");
            using (var clear = SKBitmap.Decode(PngWithAlpha(40, 20, 0)))
                Assert.IsTrue(RenderHistoryImages.HasTransparency(clear));
            using (var half = SKBitmap.Decode(PngWithAlpha(40, 20, 128)))
                Assert.IsTrue(RenderHistoryImages.HasTransparency(half));
            Assert.IsFalse(RenderHistoryImages.HasTransparency(null));
        }

        [TestMethod]
        public void HasTransparency_FindsASingleTransparentPixel()
        {
            using (var bmp = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Bgra8888, SKAlphaType.Premul)))
            {
                bmp.Erase(SKColors.Red);
                Assert.IsFalse(RenderHistoryImages.HasTransparency(bmp));
                bmp.SetPixel(63, 63, new SKColor(0, 0, 0, 0));
                Assert.IsTrue(RenderHistoryImages.HasTransparency(bmp));
            }
        }

        [TestMethod]
        public void TryCreate_TransparentRenderGetsAPngPreviewThatKeepsItsAlpha()
        {
            string dir = Path.Combine(Path.GetTempPath(), "bf-alpha-" + Guid.NewGuid().ToString("N"));
            try
            {
                string thumb = Path.Combine(dir, "thumbs", "a.png");
                string preview = Path.Combine(dir, "previews", "a.jpg");
                Assert.IsTrue(RenderHistoryImages.TryCreate(PngWithAlpha(100, 50, 0), thumb, preview, out _, out _, out string written));
                Assert.AreEqual(Path.Combine(dir, "previews", "a.png"), written);
                Assert.IsFalse(File.Exists(preview), "no JPEG (which would turn transparency black)");
                using (var p = SKBitmap.Decode(written))
                    Assert.AreEqual(0, p.GetPixel(10, 10).Alpha);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [TestMethod]
        public void TryCreate_OpaqueRenderStaysAJpegPreview()
        {
            string dir = Path.Combine(Path.GetTempPath(), "bf-alpha-" + Guid.NewGuid().ToString("N"));
            try
            {
                string preview = Path.Combine(dir, "previews", "a.jpg");
                Assert.IsTrue(RenderHistoryImages.TryCreate(PngWithAlpha(100, 50, 255), null, preview, out _, out _, out string written));
                Assert.AreEqual(preview, written);
                Assert.IsTrue(File.Exists(preview));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ------------------------------------------------------------ 8: history card status

        [TestMethod]
        public void HistoryStatus_EntryWithoutAnyImageSaysNoImageCaptured()
        {
            var entry = new RenderHistoryEntry();
            Assert.AreEqual("No image captured", RenderHistoryItem.DescribeStatus(entry, false, false));
        }

        [TestMethod]
        public void HistoryStatus_VanishedPreviewSaysNoLongerAvailable()
        {
            var entry = new RenderHistoryEntry { PreviewFile = "previews/x.jpg" };
            Assert.AreEqual("Image no longer available", RenderHistoryItem.DescribeStatus(entry, false, false));
        }

        [TestMethod]
        public void HistoryStatus_MissingSavedFileAndHealthyEntries()
        {
            var saved = new RenderHistoryEntry { OutputPath = "x.png", PreviewFile = "p.jpg" };
            Assert.AreEqual("Saved file is missing, opening the preview copy", RenderHistoryItem.DescribeStatus(saved, false, true));
            Assert.AreEqual("Saved file is missing", RenderHistoryItem.DescribeStatus(saved, false, false));
            Assert.IsNull(RenderHistoryItem.DescribeStatus(saved, true, true));
            Assert.IsNull(RenderHistoryItem.DescribeStatus(new RenderHistoryEntry { IsAnimation = true }, false, false));
        }

        // ------------------------------------------------------------ 9: zoom anchor

        [TestMethod]
        public void ClampAnchor_PicksTheNearestPointOfTheImage()
        {
            var t = new ViewTransform(1, 100, 50); //image 200x100 covers x 100..300, y 50..150
            Assert.AreEqual((100.0, 50.0), ZoomMath.ClampAnchorToImage(t, 0, 0, 200, 100));
            Assert.AreEqual((300.0, 150.0), ZoomMath.ClampAnchorToImage(t, 900, 900, 200, 100));
            Assert.AreEqual((150.0, 50.0), ZoomMath.ClampAnchorToImage(t, 150, -40, 200, 100));
            Assert.AreEqual((120.0, 70.0), ZoomMath.ClampAnchorToImage(t, 120, 70, 200, 100));
        }

        [TestMethod]
        public void ZoomWithCursorOutsideTheImage_StaysAnchoredOnTheImageEdge()
        {
            //Fit view 1000x600, image 2000x1000 -> scale 0.5, offset (0,50): the image covers x 0..1000, y 50..550
            ViewTransform t = ZoomMath.Fit(1000, 600, 2000, 1000);
            var (ax, ay) = ZoomMath.ClampAnchorToImage(t, 980, 590, 2000, 1000);
            Assert.AreEqual(980, ax, 1e-9);
            Assert.AreEqual(550, ay, 1e-9); //the cursor is below the image: its bottom edge is the anchor
            ViewTransform z = ZoomMath.ZoomAround(t, 4, ax, ay);
            //The image point under the anchor (right/bottom area) did not move on screen
            var (ix, iy) = t.ToImage(ax, ay);
            var (vx, vy) = z.ToView(ix, iy);
            Assert.AreEqual(ax, vx, 1e-9);
            Assert.AreEqual(ay, vy, 1e-9);
            //The raw cursor would have moved the image's bottom edge away from where the user looks
            ViewTransform raw = ZoomMath.ZoomAround(t, 4, 980, 590);
            Assert.AreEqual(ay, z.ToView(ix, 1000).Y, 1e-9);
            Assert.AreNotEqual(ay, raw.ToView(ix, 1000).Y, 1.0);
        }
    }
}
