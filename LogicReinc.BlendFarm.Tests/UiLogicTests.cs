using Avalonia.Input;
using LogicReinc.BlendFarm.Objects;
using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LogicReinc.BlendFarm.Tests
{
    /// <summary>Pure UI-support logic: output name warnings, presets, log filtering, shortcuts, drop filtering, power/notify decisions.</summary>
    [TestClass]
    public class UiLogicTests
    {
        #region Output name validation

        [TestMethod]
        public void AnimationFormat_Valid_NoWarnings()
        {
            Assert.AreEqual(0, OutputNameValidator.ValidateAnimationFormat("frame_####.png", "png").Count);
            Assert.AreEqual(0, OutputNameValidator.ValidateAnimationFormat("{scene}_###.exr", "exr").Count);
        }

        [TestMethod]
        public void AnimationFormat_WithoutHash_WarnsAboutOverwrite()
        {
            var warnings = OutputNameValidator.ValidateAnimationFormat("frame.png", "png");
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "overwrites");
        }

        [TestMethod]
        public void AnimationFormat_FrameTokenCountsAsFrameNumber()
        {
            Assert.AreEqual(0, OutputNameValidator.ValidateAnimationFormat("shot_{frame}.png", "png").Count);
        }

        [TestMethod]
        public void AnimationFormat_Empty_Warns()
        {
            Assert.AreEqual(1, OutputNameValidator.ValidateAnimationFormat("  ", "png").Count);
            Assert.AreEqual(1, OutputNameValidator.ValidateAnimationFormat(null, "png").Count);
        }

        [TestMethod]
        public void AnimationFormat_InvalidCharacters_AreListed()
        {
            var warnings = OutputNameValidator.ValidateAnimationFormat("a:b?_###.png", "png");
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "':'");
            StringAssert.Contains(warnings[0], "'?'");
        }

        [TestMethod]
        public void AnimationFormat_TokenBracesAreNotInvalid()
        {
            Assert.AreEqual(0, OutputNameValidator.ValidateAnimationFormat("{scene}_{camera}_###.png", "png").Count);
        }

        [TestMethod]
        public void AnimationFormat_UnknownToken_Warns()
        {
            var warnings = OutputNameValidator.ValidateAnimationFormat("{shot}_###.png", "png");
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "{shot}");
        }

        [TestMethod]
        public void AnimationFormat_ExtensionMismatch_Warns()
        {
            var warnings = OutputNameValidator.ValidateAnimationFormat("frame_###.jpg", "png");
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "doesn't match");
        }

        [TestMethod]
        public void AnimationFormat_EquivalentExtensions_Match()
        {
            Assert.AreEqual(0, OutputNameValidator.ValidateAnimationFormat("f_###.jpeg", "jpg").Count);
            Assert.AreEqual(0, OutputNameValidator.ValidateAnimationFormat("f_###.TIFF", "tif").Count);
        }

        [TestMethod]
        public void AnimationFormat_MissingExtension_Warns()
        {
            var warnings = OutputNameValidator.ValidateAnimationFormat("frame_###", "png");
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "No file extension");
        }

        [TestMethod]
        public void AnimationFormat_DotInSceneNameIsNotAnExtension()
        {
            // "Shot.v2_###" doesn't end in an image extension, so it counts as extension-less
            Assert.IsNull(OutputNameValidator.GetImageExtension("Shot.v2_###"));
            Assert.AreEqual("PNG", OutputNameValidator.GetImageExtension("Shot.v2_###.PNG"));
        }

        [TestMethod]
        public void OutputName_EmptyAndExtensionlessAreFine()
        {
            Assert.AreEqual(0, OutputNameValidator.ValidateOutputName("", "png").Count);
            Assert.AreEqual(0, OutputNameValidator.ValidateOutputName("{scene}_###", "png").Count);
            Assert.AreEqual(0, OutputNameValidator.ValidateOutputName("still", "png").Count);
        }

        [TestMethod]
        public void OutputName_BadCharAndMismatch()
        {
            Assert.AreEqual(1, OutputNameValidator.ValidateOutputName("a|b", "png").Count);
            Assert.AreEqual(1, OutputNameValidator.ValidateOutputName("still.exr", "png").Count);
        }

        [TestMethod]
        public void Validator_AgreesWithBlockingValidator_OnInvalidChars()
        {
            // Whatever the app refuses to render (TryValidateTemplate) must also be flagged inline
            foreach (string template in new[] { "bad:name_#.png", "x/y_#.png", "a*b_#" })
            {
                Assert.IsFalse(OutputNaming.TryValidateTemplate(template, out _));
                Assert.IsTrue(OutputNameValidator.ValidateAnimationFormat(template, "png").Any(w => w.Contains("Invalid filename")), template);
            }
        }

        #endregion

        #region Presets

        private static OpenBlenderProject NewProject() => new OpenBlenderProject("test.blend")
        {
            RenderWidth = 1920,
            RenderHeight = 1080,
            RenderScale = 100,
            Samples = 128,
            ChunkSize = 256,
            Denoiser = "Inherit",
            Engine = EngineType.Cycles,
            RenderFormat = "PNG"
        };

        [TestMethod]
        public void Preset_Preview_AppliesScaleAndSamples()
        {
            var project = NewProject();
            int? baseline = null;
            var changed = RenderPreset.BuiltIns[0].ApplyTo(project, ref baseline);
            Assert.AreEqual(50, project.RenderScale);
            Assert.AreEqual(16, project.Samples);
            Assert.AreEqual(128, baseline);
            CollectionAssert.AreEquivalent(new[] { "RenderScale", "Samples" }, changed);
            // untouched
            Assert.AreEqual(1920, project.RenderWidth);
            Assert.AreEqual("PNG", project.RenderFormat);
        }

        [TestMethod]
        public void Preset_Final_RestoresProjectSamples()
        {
            var project = NewProject();
            int? baseline = null;
            RenderPreset.BuiltIns[0].ApplyTo(project, ref baseline);
            RenderPreset.BuiltIns[1].ApplyTo(project, ref baseline);
            Assert.AreEqual(100, project.RenderScale);
            Assert.AreEqual(128, project.Samples);
            Assert.IsNull(baseline);
        }

        [TestMethod]
        public void Preset_PreviewTwice_KeepsOriginalBaseline()
        {
            var project = NewProject();
            int? baseline = null;
            RenderPreset.BuiltIns[0].ApplyTo(project, ref baseline);
            RenderPreset.BuiltIns[0].ApplyTo(project, ref baseline);
            Assert.AreEqual(128, baseline);
        }

        [TestMethod]
        public void Preset_Final_WithoutBaseline_LeavesSamples()
        {
            var project = NewProject();
            int? baseline = null;
            RenderPreset.BuiltIns[1].ApplyTo(project, ref baseline);
            Assert.AreEqual(128, project.Samples);
        }

        [TestMethod]
        public void Preset_CaptureAndApply_RoundTripsEverything()
        {
            var source = NewProject();
            source.RenderWidth = 3840; source.RenderHeight = 2160; source.RenderScale = 75; source.Samples = 300;
            source.ChunkSize = 512; source.Denoiser = "OPTIX"; source.Engine = EngineType.Eevee; source.RenderFormat = "OPEN_EXR";
            RenderPreset preset = RenderPreset.Capture("  Mine ", source, "SplitChunked");

            var target = NewProject();
            int? baseline = null;
            preset.ApplyTo(target, ref baseline);
            Assert.AreEqual("Mine", preset.Name);
            Assert.AreEqual(3840, target.RenderWidth);
            Assert.AreEqual(2160, target.RenderHeight);
            Assert.AreEqual(75, target.RenderScale);
            Assert.AreEqual(300, target.Samples);
            Assert.AreEqual(512, target.ChunkSize);
            Assert.AreEqual("OPTIX", target.Denoiser);
            Assert.AreEqual(EngineType.Eevee, target.Engine);
            Assert.AreEqual("OPEN_EXR", target.RenderFormat);
            Assert.AreEqual("SplitChunked", preset.Strategy);
        }

        [TestMethod]
        public void Preset_SerializesAndDeserializes()
        {
            var settings = new BlendFarmSettings();
            RenderPreset.Upsert(settings.UserPresets, RenderPreset.Capture("Night", NewProject(), "SplitChunked"));
            string json = JsonSerializer.Serialize(settings);
            var back = JsonSerializer.Deserialize<BlendFarmSettings>(json);
            Assert.AreEqual(1, back.UserPresets.Count);
            Assert.AreEqual("Night", back.UserPresets[0].Name);
            Assert.AreEqual(128, back.UserPresets[0].Samples);
            Assert.AreEqual("SplitChunked", back.UserPresets[0].Strategy);
            Assert.IsFalse(back.UserPresets[0].BuiltIn);
        }

        [TestMethod]
        public void Preset_Defaults_PersistedFeatureFlags()
        {
            var back = JsonSerializer.Deserialize<BlendFarmSettings>("{}");
            Assert.IsTrue(back.PreventSleepWhileRendering);
            Assert.IsTrue(back.DesktopNotificationEnabled);
            Assert.IsTrue(back.PlayCompletionSound);
            Assert.IsNotNull(back.UserPresets);
        }

        [TestMethod]
        public void Preset_UpsertReplacesSameNameCaseInsensitive_AndRemoves()
        {
            var list = new List<RenderPreset>();
            Assert.IsTrue(RenderPreset.Upsert(list, new RenderPreset { Name = "A", Samples = 1 }));
            Assert.IsTrue(RenderPreset.Upsert(list, new RenderPreset { Name = "a", Samples = 2 }));
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(2, list[0].Samples);
            Assert.IsTrue(RenderPreset.Remove(list, "A"));
            Assert.AreEqual(0, list.Count);
            Assert.IsFalse(RenderPreset.Remove(list, "A"));
        }

        [TestMethod]
        public void Preset_ReservedAndEmptyNamesRejected()
        {
            var list = new List<RenderPreset>();
            Assert.IsFalse(RenderPreset.Upsert(list, new RenderPreset { Name = RenderPreset.PreviewName }));
            Assert.IsFalse(RenderPreset.Upsert(list, new RenderPreset { Name = " " }));
            Assert.IsFalse(RenderPreset.Upsert(list, null));
            Assert.AreEqual(0, list.Count);
        }

        [TestMethod]
        public void Preset_Combine_BuiltInsFirst_NoDuplicates()
        {
            var user = new List<RenderPreset> { new RenderPreset { Name = "Mine" }, new RenderPreset { Name = RenderPreset.FinalName }, new RenderPreset { Name = "mine" } };
            var all = RenderPreset.Combine(user);
            Assert.AreEqual(3, all.Count);
            Assert.IsTrue(all[0].BuiltIn && all[1].BuiltIn);
            Assert.AreEqual("Mine", all[2].Name);
        }

        [TestMethod]
        public void Preset_InvalidValuesIgnored()
        {
            var project = NewProject();
            int? baseline = null;
            new RenderPreset { Name = "bad", Width = 0, RenderScale = -5, Samples = 0, Engine = "NotAnEngine", ChunkSize = 0 }.ApplyTo(project, ref baseline);
            Assert.AreEqual(1920, project.RenderWidth);
            Assert.AreEqual(100, project.RenderScale);
            Assert.AreEqual(128, project.Samples);
            Assert.AreEqual(EngineType.Cycles, project.Engine);
            Assert.AreEqual(256, project.ChunkSize);
        }

        #endregion

        #region Activity log filter

        private static List<RenderActivityLogEntry> SampleLog() => new List<RenderActivityLogEntry>
        {
            new RenderActivityLogEntry("Ready"),
            new RenderActivityLogEntry("Local connected", ActivityLogType.Success),
            new RenderActivityLogEntry("Local: rendering chunk 3"),
            new RenderActivityLogEntry("Studio / PC2 disconnected", ActivityLogType.Warning),
            new RenderActivityLogEntry("Studio / PC2: failed to sync", ActivityLogType.Error),
            new RenderActivityLogEntry("Render failed: timeout", ActivityLogType.Error),
            new RenderActivityLogEntry("Localhost removed", ActivityLogType.Warning)
        };

        private static Dictionary<string, string> Nodes() => new Dictionary<string, string>
        {
            ["Local"] = "Local",
            ["Studio"] = "Studio",
            ["Studio / PC2"] = "Studio"
        };

        [TestMethod]
        public void LogFilter_DefaultShowsEverything()
        {
            var filter = new ActivityLogFilter();
            Assert.IsFalse(filter.IsActive);
            Assert.AreEqual(7, filter.Apply(SampleLog(), Nodes()).Count);
        }

        [TestMethod]
        public void LogFilter_ByLevel()
        {
            var log = SampleLog();
            Assert.AreEqual(3, new ActivityLogFilter { Level = ActivityLogLevelFilter.Info }.Apply(log, Nodes()).Count);      // Info + Success
            Assert.AreEqual(2, new ActivityLogFilter { Level = ActivityLogLevelFilter.Warnings }.Apply(log, Nodes()).Count);
            Assert.AreEqual(2, new ActivityLogFilter { Level = ActivityLogLevelFilter.Errors }.Apply(log, Nodes()).Count);
        }

        [TestMethod]
        public void LogFilter_ByNode_UsesTitleBoundariesAndAliases()
        {
            var log = SampleLog();
            var local = new ActivityLogFilter { Node = "Local" }.Apply(log, Nodes());
            Assert.AreEqual(2, local.Count); // "Localhost removed" is NOT node "Local"
            var studio = new ActivityLogFilter { Node = "Studio" }.Apply(log, Nodes());
            Assert.AreEqual(2, studio.Count); // old and new title both map to the node
        }

        [TestMethod]
        public void LogFilter_BySearch_CaseInsensitive()
        {
            var result = new ActivityLogFilter { Search = " TIMEOUT " }.Apply(SampleLog(), Nodes());
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Render failed: timeout", result[0].Message);
        }

        [TestMethod]
        public void LogFilter_CombinesAllCriteria()
        {
            var filter = new ActivityLogFilter { Level = ActivityLogLevelFilter.Errors, Node = "Studio", Search = "sync" };
            var result = filter.Apply(SampleLog(), Nodes());
            Assert.AreEqual(1, result.Count);
            Assert.IsTrue(filter.IsActive);
        }

        [TestMethod]
        public void LogFilter_FindNode_LongestTitleWins()
        {
            Assert.AreEqual("Studio", ActivityLogFilter.FindNode("Studio / PC2: ok", Nodes()));
            Assert.AreEqual("Studio", ActivityLogFilter.FindNode("Studio added", Nodes()));
            Assert.IsNull(ActivityLogFilter.FindNode("Render started", Nodes()));
            Assert.IsNull(ActivityLogFilter.FindNode("anything", null));
        }

        [TestMethod]
        public void LogFilter_FormatsEntriesForCopyAndExport()
        {
            var entry = new RenderActivityLogEntry("Disk full", ActivityLogType.Error);
            string line = ActivityLogFilter.FormatEntry(entry);
            Assert.AreEqual(entry.Time + " [ERROR] Disk full", line);
            string text = ActivityLogFilter.FormatEntries(new[] { entry, new RenderActivityLogEntry("ok") });
            Assert.AreEqual(2, text.Split('\n', System.StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.AreEqual(2000, ActivityLogFilter.MaxEntries);
        }

        #endregion

        #region Shortcuts, drop filtering, power, notification decisions

        [TestMethod]
        public void Shortcuts_AreUnique_AndComplete()
        {
            var keys = ShortcutCatalog.All.Select(s => s.Keys).ToList();
            CollectionAssert.AreEquivalent(keys, keys.Distinct().ToList());
            foreach (string expected in new[] { "Ctrl+O", "Ctrl+R", "Ctrl+Shift+R", "Esc", "Ctrl+S", "Ctrl+Q", "Ctrl+Shift+Q", "Ctrl+W", "F1" })
                CollectionAssert.Contains(keys, expected);
            Assert.AreEqual("Render the frame range (Ctrl+Shift+R)", ShortcutCatalog.Tooltip("Render the frame range", ShortcutCatalog.RenderAnimation));
        }

        [TestMethod]
        public void Shortcuts_ResolveKeys()
        {
            var ctrl = KeyModifiers.Control;
            var ctrlShift = KeyModifiers.Control | KeyModifiers.Shift;
            Assert.AreEqual(ShortcutAction.OpenProject, ShortcutCatalog.Resolve(Key.O, ctrl));
            Assert.AreEqual(ShortcutAction.RenderNow, ShortcutCatalog.Resolve(Key.R, ctrl));
            Assert.AreEqual(ShortcutAction.RenderAnimation, ShortcutCatalog.Resolve(Key.R, ctrlShift));
            Assert.AreEqual(ShortcutAction.SaveImage, ShortcutCatalog.Resolve(Key.S, ctrl));
            Assert.AreEqual(ShortcutAction.AddToQueue, ShortcutCatalog.Resolve(Key.Q, ctrl));
            Assert.AreEqual(ShortcutAction.AddAnimationToQueue, ShortcutCatalog.Resolve(Key.Q, ctrlShift));
            Assert.AreEqual(ShortcutAction.CloseProject, ShortcutCatalog.Resolve(Key.W, ctrl));
            Assert.AreEqual(ShortcutAction.CancelRender, ShortcutCatalog.Resolve(Key.Escape, KeyModifiers.None));
            Assert.AreEqual(ShortcutAction.ShowHelp, ShortcutCatalog.Resolve(Key.F1, KeyModifiers.None));
            // Cmd works like Ctrl on macOS
            Assert.AreEqual(ShortcutAction.RenderNow, ShortcutCatalog.Resolve(Key.R, KeyModifiers.Meta));
        }

        [TestMethod]
        public void Shortcuts_DoNotStealTextEditingKeys()
        {
            // plain letters, Ctrl+C/V/X/A/Z/Y, Alt combos, Shift+Esc and Ctrl+Esc are never claimed
            Assert.IsNull(ShortcutCatalog.Resolve(Key.R, KeyModifiers.None));
            Assert.IsNull(ShortcutCatalog.Resolve(Key.S, KeyModifiers.Shift));
            foreach (Key key in new[] { Key.C, Key.V, Key.X, Key.A, Key.Z, Key.Y, Key.Back, Key.Delete, Key.Enter })
                Assert.IsNull(ShortcutCatalog.Resolve(key, KeyModifiers.Control), key.ToString());
            Assert.IsNull(ShortcutCatalog.Resolve(Key.R, KeyModifiers.Control | KeyModifiers.Alt));
            Assert.IsNull(ShortcutCatalog.Resolve(Key.Escape, KeyModifiers.Shift));
            Assert.IsNull(ShortcutCatalog.Resolve(Key.Escape, KeyModifiers.Control));
            Assert.IsNull(ShortcutCatalog.Resolve(Key.O, KeyModifiers.Control | KeyModifiers.Shift));
            Assert.IsNull(ShortcutCatalog.Resolve(Key.W, KeyModifiers.Control | KeyModifiers.Shift));
            Assert.IsNull(ShortcutCatalog.Resolve(Key.F1, KeyModifiers.Control));
        }

        [TestMethod]
        public void DroppedFiles_KeepsOnlyBlendFiles()
        {
            var result = DroppedFiles.BlendFiles(new[] { "C:\\a\\scene.blend", "C:\\a\\notes.txt", "D:\\x\\UP.BLEND", "C:\\a\\scene.blend1", null, "" });
            CollectionAssert.AreEqual(new[] { "C:\\a\\scene.blend", "D:\\x\\UP.BLEND" }, result);
            Assert.AreEqual(0, DroppedFiles.BlendFiles(null).Count);
        }

        [TestMethod]
        public void DroppedFiles_SamePath()
        {
            Assert.IsFalse(DroppedFiles.SamePath(null, "a.blend"));
            Assert.IsTrue(DroppedFiles.SamePath("a.blend", "./a.blend"));
        }

        [TestMethod]
        public void Power_KeepAwakeDecision()
        {
            Assert.IsFalse(PowerManagement.ShouldKeepAwake(true, false, false, false));
            Assert.IsTrue(PowerManagement.ShouldKeepAwake(true, true, false, false));
            Assert.IsTrue(PowerManagement.ShouldKeepAwake(true, false, true, false));
            Assert.IsTrue(PowerManagement.ShouldKeepAwake(true, false, false, true));
            Assert.IsFalse(PowerManagement.ShouldKeepAwake(false, true, true, true));
        }

        [TestMethod]
        public void Power_SetKeepAwake_TracksStateAndIsIdempotent()
        {
            Assert.IsFalse(PowerManagement.SetKeepAwake(false));
            Assert.IsTrue(PowerManagement.SetKeepAwake(true));
            Assert.IsTrue(PowerManagement.KeepingAwake);
            Assert.IsFalse(PowerManagement.SetKeepAwake(true));
            Assert.IsTrue(PowerManagement.SetKeepAwake(false));
            Assert.IsFalse(PowerManagement.KeepingAwake);
        }

        [TestMethod]
        public void Notifier_Decisions()
        {
            Assert.IsFalse(DesktopNotifier.ShouldNotify(false, false, false));
            Assert.IsTrue(DesktopNotifier.ShouldNotify(true, false, true));   // always mode
            Assert.IsFalse(DesktopNotifier.ShouldNotify(true, true, true));   // focused + only-unfocused
            Assert.IsTrue(DesktopNotifier.ShouldNotify(true, true, false));
        }

        [TestMethod]
        public void Notifier_Text()
        {
            var ok = new RenderCompletionInfo { Success = true, SceneName = "Demo", Elapsed = System.TimeSpan.FromSeconds(95), IsAnimation = true, FrameCount = 10 };
            Assert.AreEqual("Animation complete", DesktopNotifier.BuildTitle(ok));
            Assert.AreEqual("Demo finished in 1m 35s, 10 frames", DesktopNotifier.BuildBody(ok));
            var bad = new RenderCompletionInfo { Success = false, SceneName = "Demo", EngineOrError = "node lost" };
            Assert.AreEqual("Render failed", DesktopNotifier.BuildTitle(bad));
            Assert.AreEqual("Demo: node lost", DesktopNotifier.BuildBody(bad));
        }

        #endregion
    }
}
