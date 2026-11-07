using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Reflection;

namespace LogicReinc.BlendFarm.Tests
{
    /// <summary>Regression tests for the second review round (layout clamping, history data).</summary>
    [TestClass]
    public class ReviewRound2Tests
    {
        // ------------------------------------------------------------ window columns

        [DataTestMethod]
        [DataRow(380, 460, 1400)]
        [DataRow(380, 460, 1100)]
        [DataRow(380, 460, 1050)]
        [DataRow(680, 800, 1050)]
        [DataRow(680, 800, 1900)]
        [DataRow(300, 390, 1050)]
        public void ColumnClamp_KeepsTheCentreColumnUsable(double left, double right, double window)
        {
            var (l, r) = WindowColumnLayout.Clamp(left, right, window);
            Assert.IsTrue(l >= WindowColumnLayout.LeftMin - 1e-9, "left below minimum: " + l);
            Assert.IsTrue(r >= WindowColumnLayout.RightMin - 1e-9, "right below minimum: " + r);
            Assert.IsTrue(l <= left + 1e-9 && r <= right + 1e-9, "a column grew");
            double centre = window - WindowColumnLayout.Splitters - l - r;
            Assert.IsTrue(centre >= WindowColumnLayout.CenterMin - 1e-6, $"centre {centre} too narrow at {window}");
        }

        [TestMethod]
        public void ColumnClamp_LeavesRoomyWindowsAlone()
        {
            Assert.AreEqual((380.0, 460.0), WindowColumnLayout.Clamp(380, 460, 1900));
            Assert.AreEqual((380.0, 460.0), WindowColumnLayout.Clamp(380, 460, 1400));
        }

        [TestMethod]
        public void ColumnClamp_ShrinksBothColumnsProportionally()
        {
            var (l, r) = WindowColumnLayout.Clamp(380, 460, 1100);
            Assert.IsTrue(l < 380 && r < 460);
            Assert.AreEqual(1100 - 12 - 280, l + r, 1e-6);
        }

        [TestMethod]
        public void ColumnClamp_ClampsBelowTheSumOfTheMinimums()
        {
            //Impossible to fit a 280 centre below ~982px: the side columns stop at their minimums
            var (l, r) = WindowColumnLayout.Clamp(380, 460, 800);
            Assert.AreEqual(WindowColumnLayout.LeftMin, l, 1e-9);
            Assert.AreEqual(WindowColumnLayout.RightMin, r, 1e-9);
        }

        // ------------------------------------------------------------ history data

        private static QueueItem Item(string scene, string projectScene = null, params string[] scenes)
        {
            var project = (OpenBlenderProject)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(OpenBlenderProject));
            project.BlendFile = "ProjectName.blend";
            project.Scene = projectScene ?? "";
            typeof(OpenBlenderProject).GetProperty(nameof(OpenBlenderProject.ScenesAvailable)).SetValue(project, new System.Collections.Generic.List<string>(scenes));
            return new QueueItem(null, project, new RenderManagerSettings { Scene = scene, Frame = 4 });
        }

        [TestMethod]
        public void SceneLabel_PrefersTheRealSceneOverTheProjectName()
        {
            Assert.AreEqual("Shot_A", QueueItem.SceneLabel(new RenderManagerSettings { Scene = "Shot_A" }, Item("Shot_A").Project));
            Assert.AreEqual("Selected", QueueItem.SceneLabel(new RenderManagerSettings(), Item("", "Selected").Project));
            Assert.AreEqual("Scene", QueueItem.SceneLabel(new RenderManagerSettings(), Item("", "", "", "Scene", "Other").Project));
            Assert.AreEqual("ProjectName", QueueItem.SceneLabel(new RenderManagerSettings(), Item("").Project));
            Assert.IsNull(QueueItem.SceneLabel(null, null));
        }

        [TestMethod]
        public void QueueCompletion_UsesTheRealSceneAndSumsAllRuns()
        {
            QueueItem item = Item("", "", "Scene");
            MethodInfo endRun = typeof(QueueItem).GetMethod("EndRun", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo build = typeof(QueueItem).GetMethod("BuildCompletionInfo", BindingFlags.NonPublic | BindingFlags.Instance);
            //A first run that was stopped after 10s, then the resumed run took 5s
            endRun.Invoke(item, new object[] { TimeSpan.FromSeconds(10) });
            var info = (RenderCompletionInfo)build.Invoke(item, new object[] { true, TimeSpan.FromSeconds(5), null, null });
            Assert.AreEqual("Scene", info.SceneName);
            Assert.AreEqual(TimeSpan.FromSeconds(15), info.Elapsed);
            Assert.AreEqual(4, info.FrameStart);
            Assert.AreEqual(4, info.FrameEnd);
            Assert.AreEqual(1, info.FrameCount);
        }

        [TestMethod]
        public void RetryFromScratch_ForgetsEarlierRunTime()
        {
            QueueItem item = Item("Scene");
            typeof(QueueItem).GetMethod("EndRun", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(item, new object[] { TimeSpan.FromSeconds(10) });
            Assert.AreEqual(TimeSpan.FromSeconds(10), item.PriorRunTime);
            item.Exception = "boom";
            Assert.IsTrue(item.ResetForRetry());
            Assert.AreEqual(TimeSpan.Zero, item.PriorRunTime);
        }
    }
}
