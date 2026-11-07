using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LogicReinc.BlendFarm.Objects
{
    public class ShortcutInfo
    {
        public string Keys { get; }
        public string Description { get; }
        public ShortcutInfo(string keys, string description) { Keys = keys; Description = description; }
    }

    public enum ShortcutAction
    {
        OpenProject,
        RenderNow,
        RenderAnimation,
        CancelRender,
        SaveImage,
        AddToQueue,
        AddAnimationToQueue,
        CloseProject,
        ShowHelp
    }

    public enum EscapeDecision
    {
        /// <summary>Leave the key to the focused control</summary>
        Ignore,
        /// <summary>Cancel the render right away</summary>
        Cancel,
        /// <summary>Ask before cancelling a long-running render</summary>
        Confirm
    }

    /// <summary>Single list of the main-window shortcuts: drives the help window and the button tooltips.</summary>
    public static class ShortcutCatalog
    {
        public const string OpenProject = "Ctrl+O";
        public const string RenderNow = "Ctrl+R";
        public const string RenderAnimation = "Ctrl+Shift+R";
        public const string Cancel = "Esc";
        public const string SaveImage = "Ctrl+S";
        public const string AddToQueue = "Ctrl+Q";
        public const string AddAnimationToQueue = "Ctrl+Shift+Q";
        public const string CloseProject = "Ctrl+W";
        public const string Help = "F1";

        public static IReadOnlyList<ShortcutInfo> All { get; } = new List<ShortcutInfo>
        {
            new ShortcutInfo(OpenProject, "Open a .blend project"),
            new ShortcutInfo(RenderNow, "Render Now (still)"),
            new ShortcutInfo(RenderAnimation, "Render Animation"),
            new ShortcutInfo(Cancel, "Cancel the current render (asks first after 5 seconds, ignored while typing in a text box)"),
            new ShortcutInfo(SaveImage, "Save Image"),
            new ShortcutInfo(AddToQueue, "Add to Queue"),
            new ShortcutInfo(AddAnimationToQueue, "Add animation to Queue"),
            new ShortcutInfo(CloseProject, "Close the current project tab"),
            new ShortcutInfo(Help, "Show this list")
        };

        /// <summary>
        /// Maps a key press to a shortcut. Ctrl (or Cmd on macOS) is the command key; Alt combinations never match,
        /// so text editing and menu keys are left alone.
        /// </summary>
        public static ShortcutAction? Resolve(Key key, KeyModifiers modifiers)
        {
            if (modifiers.HasFlag(KeyModifiers.Alt))
                return null;
            bool command = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
            bool shift = modifiers.HasFlag(KeyModifiers.Shift);
            if (!command)
            {
                if (shift) return null;
                if (key == Key.F1) return ShortcutAction.ShowHelp;
                if (key == Key.Escape) return ShortcutAction.CancelRender;
                return null;
            }
            switch (key)
            {
                case Key.O: return shift ? (ShortcutAction?)null : ShortcutAction.OpenProject;
                case Key.R: return shift ? ShortcutAction.RenderAnimation : ShortcutAction.RenderNow;
                case Key.S: return shift ? (ShortcutAction?)null : ShortcutAction.SaveImage;
                case Key.Q: return shift ? ShortcutAction.AddAnimationToQueue : ShortcutAction.AddToQueue;
                case Key.W: return shift ? (ShortcutAction?)null : ShortcutAction.CloseProject;
                default: return null;
            }
        }

        /// <summary>A render running longer than this asks before Esc cancels it.</summary>
        public static readonly TimeSpan CancelConfirmAfter = TimeSpan.FromSeconds(5);

        /// <summary>What a pressed Esc key does while no text editing or popup claims it.</summary>
        public static EscapeDecision DecideEscape(bool isRendering, bool focusInTextBox, bool dropDownOpen, TimeSpan renderElapsed)
        {
            //Esc belongs to a text box (it reverts/cancels the edit) and to an open combo box popup
            if (!isRendering || focusInTextBox || dropDownOpen)
                return EscapeDecision.Ignore;
            return renderElapsed > CancelConfirmAfter ? EscapeDecision.Confirm : EscapeDecision.Cancel;
        }

        /// <summary>Description plus shortcut, as shown in button tooltips.</summary>
        public static string Tooltip(string description, string keys) => $"{description} ({keys})";
    }

    /// <summary>Filters drag &amp; drop file lists down to the .blend projects.</summary>
    public static class DroppedFiles
    {
        public static bool IsBlendFile(string path) =>
            !string.IsNullOrWhiteSpace(path) && string.Equals(Path.GetExtension(path.Trim()), ".blend", StringComparison.OrdinalIgnoreCase);

        public static List<string> BlendFiles(IEnumerable<string> paths) =>
            (paths ?? Enumerable.Empty<string>()).Where(IsBlendFile).Select(p => p.Trim()).ToList();

        /// <summary>Same path on disk, ignoring case and separator style (Windows semantics unless the OS is case sensitive).</summary>
        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                StringComparison cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), cmp);
            }
            catch { return false; }
        }
    }
}
