using LogicReinc.BlendFarm.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;

namespace LogicReinc.BlendFarm.Tests
{
    /// <summary>
    /// Tests for output-filename templating (OutputNaming).
    /// </summary>
    [TestClass]
    public class OutputNameTest
    {
        [TestMethod]
        public void HashBehaviorUnchanged()
        {
            // Existing contract: a run of '#' is replaced with the zero-padded frame number.
            Assert.AreEqual("007.png", OutputNaming.ApplyFrameHashes("###.png", 7, 0, 250));
            // A single '#' still pads to a minimum width of 3 (matches legacy behaviour).
            Assert.AreEqual("001.png", OutputNaming.ApplyFrameHashes("#.png", 1, 1, 1));
            // No '#' present: format returned unchanged.
            Assert.AreEqual("still.png", OutputNaming.ApplyFrameHashes("still.png", 5, 1, 10));
        }

        [TestMethod]
        public void TokensExpand()
        {
            OutputNameContext ctx = new OutputNameContext
            {
                Scene = "Shot",
                Samples = 128,
                Engine = "Cycles",
                Width = 1920,
                Height = 1080,
                Frame = 12,
                StartFrame = 1,
                EndFrame = 100
            };
            Assert.AreEqual("Shot_c128_1920x1080_012.png",
                OutputNaming.FormatOutputFileName("{scene}_c{samples}_{width}x{height}_###.png", ctx));
        }

        [TestMethod]
        public void InvalidCharsInTokenValuesSanitized()
        {
            OutputNameContext ctx = new OutputNameContext
            {
                Scene = "a/b:c",
                Frame = 1,
                StartFrame = 1,
                EndFrame = 1
            };
            string name = OutputNaming.FormatOutputFileName("{scene}_#.png", ctx);
            Assert.IsFalse(Path.GetInvalidFileNameChars().Any(c => name.Contains(c)));
        }

        [TestMethod]
        public void TokensAreCaseInsensitive()
        {
            OutputNameContext ctx = new OutputNameContext
            {
                Scene = "X",
                Frame = 1,
                StartFrame = 1,
                EndFrame = 1
            };
            Assert.AreEqual("X_001.png", OutputNaming.FormatOutputFileName("{SCENE}_#.png", ctx));
        }

        [TestMethod]
        public void ValidationRejectsInvalidChars()
        {
            Assert.IsTrue(OutputNaming.TryValidateTemplate("{scene}_###.png", out _));
            Assert.IsFalse(OutputNaming.TryValidateTemplate("bad:name_#.png", out string error));
            Assert.IsNotNull(error);
        }

        [TestMethod]
        public void HashInTokenValueDoesNotHijackPadding()
        {
            OutputNameContext ctx = new OutputNameContext { Scene = "Shot#1", Frame = 12, StartFrame = 1, EndFrame = 100 };
            Assert.AreEqual("Shot1_012.png", OutputNaming.FormatOutputFileName("{scene}_###.png", ctx));
        }

        [TestMethod]
        public void DollarInTokenValueIsLiteral()
        {
            OutputNameContext ctx = new OutputNameContext { Scene = "Cost$1", Frame = 1, StartFrame = 1, EndFrame = 1 };
            Assert.AreEqual("Cost$1_001.png", OutputNaming.FormatOutputFileName("{scene}_#.png", ctx));
        }

        [TestMethod]
        public void NegativeFramesPadDigits()
        {
            Assert.AreEqual("-005.png", OutputNaming.ApplyFrameHashes("###.png", -5, -10, 10));
        }

        [TestMethod]
        public void FindExistingFramesSkipsMissingAndEmpty()
        {
            string dir = Path.Combine(Path.GetTempPath(), "bf_existing_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "001.png"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(dir, "003.png"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(dir, "004.png"), new byte[0]); //Interrupted write, render again
                var found = OutputNaming.FindExistingFrames(dir, 1, 5, f => OutputNaming.ApplyFrameHashes("###.png", f, 1, 5));
                CollectionAssert.AreEqual(new[] { 1, 3 }, found.ToArray());
                Assert.AreEqual(0, OutputNaming.FindExistingFrames(Path.Combine(dir, "missing"), 1, 5, f => "x").Count);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void ImageExtensionDetection()
        {
            Assert.IsTrue(OutputNaming.HasImageExtension("Shot_001.png"));
            Assert.IsTrue(OutputNaming.HasImageExtension("Shot_001.EXR"));
            Assert.IsFalse(OutputNaming.HasImageExtension("Shot.v2_001"));
            Assert.IsFalse(OutputNaming.HasImageExtension("render"));
        }
    }
}
