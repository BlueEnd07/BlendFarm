using LogicReinc.BlendFarm.Controls;
using LogicReinc.BlendFarm.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;

namespace LogicReinc.BlendFarm.Tests
{
    [TestClass]
    public class ZoomMathTests
    {
        private const double Eps = 1e-9;

        [TestMethod]
        public void FitScale_UsesTheLimitingAxis()
        {
            Assert.AreEqual(0.5, ZoomMath.FitScale(1000, 1000, 2000, 1000), Eps);
            Assert.AreEqual(0.5, ZoomMath.FitScale(1000, 500, 1000, 1000), Eps);
            Assert.AreEqual(2, ZoomMath.FitScale(1000, 1000, 500, 500), Eps); //small images scale up
        }

        [TestMethod]
        public void FitScale_DegenerateSizesFallBackToOne()
        {
            Assert.AreEqual(1, ZoomMath.FitScale(0, 100, 100, 100));
            Assert.AreEqual(1, ZoomMath.FitScale(100, 100, 0, 0));
        }

        [TestMethod]
        public void Fit_CentersTheImage()
        {
            ViewTransform t = ZoomMath.Fit(1000, 600, 2000, 1000);
            Assert.AreEqual(0.5, t.Scale, Eps);
            Assert.AreEqual(0, t.OffsetX, Eps);
            Assert.AreEqual(50, t.OffsetY, Eps);
        }

        [TestMethod]
        public void ZoomAround_KeepsThePointUnderTheCursorFixed()
        {
            var t = ZoomMath.Fit(800, 600, 1600, 1200);
            double ax = 300, ay = 200;
            var before = t.ToImage(ax, ay);
            foreach (double scale in new[] { 0.1, 0.5, 1, 3.7, 16 })
            {
                var z = ZoomMath.ZoomAround(t, scale, ax, ay);
                var after = z.ToImage(ax, ay);
                Assert.AreEqual(before.X, after.X, 1e-6);
                Assert.AreEqual(before.Y, after.Y, 1e-6);
                Assert.AreEqual(scale, z.Scale, Eps);
            }
        }

        [TestMethod]
        public void ZoomAround_RoundTripsBackToTheStart()
        {
            var t = new ViewTransform(0.4, 25, -10);
            var z = ZoomMath.ZoomAround(ZoomMath.ZoomAround(t, 2.5, 123, 77), 0.4, 123, 77);
            Assert.AreEqual(t.OffsetX, z.OffsetX, 1e-9);
            Assert.AreEqual(t.OffsetY, z.OffsetY, 1e-9);
        }

        [TestMethod]
        public void ClampScale_StaysWithinLimits()
        {
            Assert.AreEqual(0.1, ZoomMath.ClampScale(0.01, 0.1, 64));
            Assert.AreEqual(64, ZoomMath.ClampScale(500, 0.1, 64));
            Assert.AreEqual(2, ZoomMath.ClampScale(2, 0.1, 64));
            Assert.AreEqual(0.1, ZoomMath.ClampScale(double.NaN, 0.1, 64));
        }

        [TestMethod]
        public void MinScale_IsBelowFitButNeverZero()
        {
            double fit = ZoomMath.FitScale(800, 600, 4000, 3000);
            double min = ZoomMath.MinScale(800, 600, 4000, 3000);
            Assert.IsTrue(min > 0 && min < fit);
            Assert.IsTrue(ZoomMath.MinScale(800, 600, 400000, 300000) >= 0.005);
        }

        [TestMethod]
        public void WheelFactor_IsSymmetric()
        {
            Assert.AreEqual(1, ZoomMath.WheelFactor(0), Eps);
            Assert.AreEqual(1, ZoomMath.WheelFactor(3) * ZoomMath.WheelFactor(-3), 1e-9);
            Assert.IsTrue(ZoomMath.WheelFactor(1) > 1);
        }

        [TestMethod]
        public void ClampOffset_KeepsPartOfTheImageVisible()
        {
            var far = new ViewTransform(1, 5000, -5000);
            var c = ZoomMath.ClampOffset(far, 800, 600, 1000, 1000, 64);
            Assert.AreEqual(800 - 64, c.OffsetX, Eps);
            Assert.AreEqual(64 - 1000, c.OffsetY, Eps);
            var inside = new ViewTransform(1, 10, 20);
            var same = ZoomMath.ClampOffset(inside, 800, 600, 1000, 1000, 64);
            Assert.AreEqual(10, same.OffsetX, Eps);
            Assert.AreEqual(20, same.OffsetY, Eps);
        }

        [TestMethod]
        public void ClampOffset_TinyImageNeedsOnlyItsOwnSizeVisible()
        {
            var c = ZoomMath.ClampOffset(new ViewTransform(1, -500, 0), 800, 600, 20, 20, 64);
            Assert.AreEqual(20 - 20, c.OffsetX, Eps);
        }

        [TestMethod]
        public void Percent_IsRelativeToDevicePixels()
        {
            Assert.AreEqual(100, ZoomMath.Percent(1, 1));
            Assert.AreEqual(150, ZoomMath.Percent(1, 1.5));
            Assert.AreEqual(50, ZoomMath.Percent(0.5, 1));
            //1:1 at 150% desktop scaling is a view scale of 1/1.5
            Assert.AreEqual(100, ZoomMath.Percent(1 / 1.5, 1.5));
        }
    }

    [TestClass]
    public class RenderHistoryStoreTests
    {
        private string _dir;

        [TestInitialize]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "bf-history-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private RenderHistoryStore NewStore(int max = 200) =>
            new RenderHistoryStore(Path.Combine(_dir, "RenderHistory.json"), Path.Combine(_dir, "History"), max);

        private static RenderHistoryEntry Entry(string name, DateTime? date = null) =>
            new RenderHistoryEntry { ProjectName = name, Date = date ?? DateTime.Now, Width = 1920, Height = 1080, Samples = 128, Engine = "CYCLES", DurationSeconds = 12.5 };

        [TestMethod]
        public void Add_PutsNewestFirst()
        {
            var store = NewStore();
            store.Add(Entry("a"));
            store.Add(Entry("b"));
            store.Add(Entry("c"));
            CollectionAssert.AreEqual(new[] { "c", "b", "a" }, store.Snapshot().Select(e => e.ProjectName).ToArray());
        }

        [TestMethod]
        public void Add_TrimsToMaxAndReportsRemoved()
        {
            var store = NewStore(3);
            for (int i = 0; i < 3; i++) store.Add(Entry("e" + i));
            var removed = store.Add(Entry("e3"));
            Assert.AreEqual(3, store.Count);
            Assert.AreEqual(1, removed.Count);
            Assert.AreEqual("e0", removed[0].ProjectName);
            CollectionAssert.AreEqual(new[] { "e3", "e2", "e1" }, store.Snapshot().Select(e => e.ProjectName).ToArray());
        }

        [TestMethod]
        public void DefaultLimitIs200()
        {
            var store = NewStore();
            for (int i = 0; i < 205; i++) store.Add(Entry("e" + i));
            Assert.AreEqual(200, store.Count);
            Assert.AreEqual("e204", store.Snapshot()[0].ProjectName);
            Assert.AreEqual("e5", store.Snapshot()[199].ProjectName);
        }

        [TestMethod]
        public void SaveAndLoad_RoundTripsAllFields()
        {
            var store = NewStore();
            var e = Entry("Scene");
            e.SceneName = "Main";
            e.BlendFile = "C:/x/y.blend";
            e.IsAnimation = true;
            e.FrameStart = 3; e.FrameEnd = 9; e.FrameCount = 7;
            e.OutputPath = "C:/out/render.png";
            e.OutputDirectory = "C:/out";
            e.ThumbnailFile = "thumbs/a.png";
            e.PreviewFile = "previews/a.jpg";
            e.NodeCount = 4;
            store.Add(e);

            var loaded = NewStore();
            loaded.Load();
            Assert.AreEqual(1, loaded.Count);
            var r = loaded.Snapshot()[0];
            Assert.AreEqual(e.Id, r.Id);
            Assert.AreEqual("Scene", r.ProjectName);
            Assert.AreEqual("Main", r.SceneName);
            Assert.AreEqual("C:/x/y.blend", r.BlendFile);
            Assert.IsTrue(r.IsAnimation);
            Assert.AreEqual(3, r.FrameStart); Assert.AreEqual(9, r.FrameEnd); Assert.AreEqual(7, r.FrameCount);
            Assert.AreEqual(1920, r.Width); Assert.AreEqual(1080, r.Height); Assert.AreEqual(128, r.Samples);
            Assert.AreEqual("CYCLES", r.Engine);
            Assert.AreEqual(12.5, r.DurationSeconds);
            Assert.AreEqual("C:/out/render.png", r.OutputPath);
            Assert.AreEqual("C:/out", r.OutputDirectory);
            Assert.AreEqual("thumbs/a.png", r.ThumbnailFile);
            Assert.AreEqual("previews/a.jpg", r.PreviewFile);
            Assert.AreEqual(4, r.NodeCount);
            Assert.AreEqual(e.Date, r.Date);
        }

        [TestMethod]
        public void Load_SortsNewestFirstAndAppliesTheLimit()
        {
            var store = NewStore();
            var now = DateTime.Now;
            //Written in scrambled order
            store.Add(Entry("mid", now.AddMinutes(-5)));
            store.Add(Entry("old", now.AddMinutes(-10)));
            store.Add(Entry("new", now));

            var loaded = NewStore(2);
            loaded.Load();
            CollectionAssert.AreEqual(new[] { "new", "mid" }, loaded.Snapshot().Select(e => e.ProjectName).ToArray());
        }

        [TestMethod]
        public void Load_MissingFileIsEmpty()
        {
            var store = NewStore();
            store.Load();
            Assert.AreEqual(0, store.Count);
        }

        [TestMethod]
        public void Load_CorruptFileStartsEmptyAndKeepsACopy()
        {
            string file = Path.Combine(_dir, "RenderHistory.json");
            File.WriteAllText(file, "{ this is not json");
            var store = NewStore();
            store.Load();
            Assert.AreEqual(0, store.Count);
            Assert.IsTrue(File.Exists(file + ".corrupt"));
            //And it recovers on the next save
            store.Add(Entry("fresh"));
            var again = NewStore();
            again.Load();
            Assert.AreEqual(1, again.Count);
        }

        [TestMethod]
        public void Remove_DeletesTheEntryAndItsThumbnailButNotTheOutput()
        {
            var store = NewStore();
            string output = Path.Combine(_dir, "render.png");
            File.WriteAllText(output, "x");
            string thumbRel = Path.Combine("thumbs", "t.png");
            Directory.CreateDirectory(store.ThumbnailDirectory);
            File.WriteAllText(store.ResolveAsset(thumbRel), "x");
            var e = Entry("a");
            e.ThumbnailFile = thumbRel;
            e.OutputPath = output;
            store.Add(e);

            Assert.IsTrue(store.Remove(e.Id));
            Assert.AreEqual(0, store.Count);
            Assert.IsFalse(File.Exists(store.ResolveAsset(thumbRel)));
            Assert.IsTrue(File.Exists(output));
            Assert.IsFalse(store.Remove(e.Id));
        }

        [TestMethod]
        public void Trim_DeletesThumbnailsOfDroppedEntries()
        {
            var store = NewStore(1);
            Directory.CreateDirectory(store.ThumbnailDirectory);
            string rel = Path.Combine("thumbs", "old.png");
            File.WriteAllText(store.ResolveAsset(rel), "x");
            var old = Entry("old");
            old.ThumbnailFile = rel;
            store.Add(old);
            store.Add(Entry("new"));
            Assert.IsFalse(File.Exists(store.ResolveAsset(rel)));
        }

        [TestMethod]
        public void Clear_RemovesEverythingIncludingFiles()
        {
            var store = NewStore();
            Directory.CreateDirectory(store.ThumbnailDirectory);
            for (int i = 0; i < 3; i++)
            {
                string rel = Path.Combine("thumbs", i + ".png");
                File.WriteAllText(store.ResolveAsset(rel), "x");
                var e = Entry("e" + i);
                e.ThumbnailFile = rel;
                store.Add(e);
            }
            store.Clear();
            Assert.AreEqual(0, store.Count);
            Assert.AreEqual(0, Directory.GetFiles(store.ThumbnailDirectory).Length);
            var again = NewStore();
            again.Load();
            Assert.AreEqual(0, again.Count);
        }

        [TestMethod]
        public void ThumbnailsOutsideTheHistoryFolderAreNeverDeleted()
        {
            var store = NewStore();
            string outside = Path.Combine(_dir, "precious.png");
            File.WriteAllText(outside, "x");
            var e = Entry("a");
            e.ThumbnailFile = outside; //absolute path, not ours
            store.Add(e);
            store.Remove(e.Id);
            Assert.IsTrue(File.Exists(outside));
        }

        [TestMethod]
        public void Update_ChangesAndPersists()
        {
            var store = NewStore();
            var e = Entry("a");
            store.Add(e);
            Assert.IsTrue(store.Update(e.Id, x => x.OutputPath = "C:/saved.png"));
            Assert.IsFalse(store.Update("nope", x => { }));
            var again = NewStore();
            again.Load();
            Assert.AreEqual("C:/saved.png", again.Snapshot()[0].OutputPath);
        }

        [TestMethod]
        public void FindNewest_ReturnsTheMostRecentMatch()
        {
            var store = NewStore();
            store.Add(Entry("a"));
            store.Add(Entry("b"));
            store.Add(Entry("a"));
            var newestA = store.FindNewest(e => e.ProjectName == "a");
            Assert.AreSame(store.Snapshot()[0], newestA);
            Assert.IsNull(store.FindNewest(e => e.ProjectName == "zzz"));
        }

        [TestMethod]
        public void Changed_FiresOnEveryMutation()
        {
            var store = NewStore();
            int count = 0;
            store.Changed += () => count++;
            var e = Entry("a");
            store.Add(e);
            store.Update(e.Id, x => x.Samples = 1);
            store.Remove(e.Id);
            store.Clear();
            Assert.AreEqual(4, count);
        }

        [TestMethod]
        public void ConcurrentAdds_DontCorruptTheFile()
        {
            var store = NewStore();
            System.Threading.Tasks.Parallel.For(0, 40, i => store.Add(Entry("p" + i)));
            Assert.AreEqual(40, store.Count);
            var again = NewStore();
            again.Load();
            Assert.AreEqual(40, again.Count);
        }
    }

    [TestClass]
    public class RenderHistoryImagesTests
    {
        private static byte[] MakePng(int w, int h, SKColor color)
        {
            using (var bmp = new SKBitmap(w, h))
            {
                bmp.Erase(color);
                using (var image = SKImage.FromBitmap(bmp))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                    return data.ToArray();
            }
        }

        [TestMethod]
        public void FitWithin_ScalesDownKeepingAspectAndNeverUp()
        {
            Assert.AreEqual((360, 180), RenderHistoryImages.FitWithin(3840, 1920, 360));
            Assert.AreEqual((180, 360), RenderHistoryImages.FitWithin(1920, 3840, 360));
            Assert.AreEqual((200, 100), RenderHistoryImages.FitWithin(200, 100, 360));
            Assert.AreEqual((360, 1), RenderHistoryImages.FitWithin(100000, 10, 360));
        }

        [TestMethod]
        public void TryCreate_WritesThumbnailAndPreview()
        {
            string dir = Path.Combine(Path.GetTempPath(), "bf-thumb-" + Guid.NewGuid().ToString("N"));
            try
            {
                string thumb = Path.Combine(dir, "thumbs", "a.png");
                string preview = Path.Combine(dir, "previews", "a.jpg");
                bool ok = RenderHistoryImages.TryCreate(MakePng(2000, 1000, SKColors.Red), thumb, preview, out int w, out int h);
                Assert.IsTrue(ok);
                Assert.AreEqual(2000, w);
                Assert.AreEqual(1000, h);
                using (var t = SKBitmap.Decode(thumb))
                {
                    Assert.AreEqual(360, t.Width);
                    Assert.AreEqual(180, t.Height);
                    Assert.AreEqual(255, t.GetPixel(10, 10).Red);
                }
                using (var p = SKBitmap.Decode(preview))
                {
                    Assert.AreEqual(1600, p.Width);
                    Assert.AreEqual(800, p.Height);
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [TestMethod]
        public void TryCreate_KeepsSmallImagesAtTheirSize()
        {
            string dir = Path.Combine(Path.GetTempPath(), "bf-thumb-" + Guid.NewGuid().ToString("N"));
            try
            {
                string thumb = Path.Combine(dir, "t.png");
                Assert.IsTrue(RenderHistoryImages.TryCreate(MakePng(64, 32, SKColors.Blue), thumb, null, out _, out _));
                using (var t = SKBitmap.Decode(thumb))
                {
                    Assert.AreEqual(64, t.Width);
                    Assert.AreEqual(32, t.Height);
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [TestMethod]
        public void TryCreate_RejectsGarbageAndNull()
        {
            Assert.IsFalse(RenderHistoryImages.TryCreate(null, "x", null, out _, out _));
            Assert.IsFalse(RenderHistoryImages.TryCreate(new byte[] { 1, 2, 3, 4 }, Path.Combine(Path.GetTempPath(), "never.png"), null, out _, out _));
        }
    }
}
