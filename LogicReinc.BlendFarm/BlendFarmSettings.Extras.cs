using LogicReinc.BlendFarm.Objects;
using System.Collections.Generic;

namespace LogicReinc.BlendFarm
{
    // Settings for keyboard/system integration, desktop notifications and render presets.
    // Kept in its own file so feature work doesn't collide with the core settings class.
    public partial class BlendFarmSettings
    {
        /// <summary>Hold the PC awake (no idle sleep) while anything is rendering.</summary>
        public bool PreventSleepWhileRendering { get; set; } = true;

        /// <summary>Show an operating-system notification when a render or queue item finishes or fails.</summary>
        public bool DesktopNotificationEnabled { get; set; } = true;

        /// <summary>Play a system sound (success / failure) when a render or queue item finishes or fails.</summary>
        public bool PlayCompletionSound { get; set; } = true;

        /// <summary>When true the desktop notification and sound only fire while the window isn't focused.</summary>
        public bool NotifyOnlyWhenUnfocused { get; set; } = true;

        /// <summary>User-defined render settings presets (the built-in ones are not stored).</summary>
        public List<RenderPreset> UserPresets { get; set; } = new List<RenderPreset>();

        /// <summary>
        /// Names of nodes switched off with "Use for rendering". Stored apart from <see cref="PastClients"/> so toggling it
        /// never turns the built-in Local or a discovered node into a saved client (which changes how it is recreated).
        /// </summary>
        public HashSet<string> DisabledNodes { get; set; } = new HashSet<string>();

        /// <summary>Last output folder used per blend file, so "Open output folder" works after a restart.</summary>
        public Dictionary<string, string> LastOutputFolders { get; set; } = new Dictionary<string, string>();

        /// <summary>True unless the node was disabled (also honours the legacy <c>HistoryClient.IsEnabled</c> flag).</summary>
        public bool IsNodeEnabled(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            DisabledNodes ??= new HashSet<string>();
            if (DisabledNodes.Contains(name)) return false;
            return !(PastClients != null && PastClients.TryGetValue(name, out var legacy) && legacy != null && !legacy.IsEnabled);
        }

        public void SetNodeEnabled(string name, bool enabled)
        {
            if (string.IsNullOrEmpty(name)) return;
            DisabledNodes ??= new HashSet<string>();
            if (enabled) DisabledNodes.Remove(name); else DisabledNodes.Add(name);
            //Migrate away from the legacy per-client flag
            if (PastClients != null && PastClients.TryGetValue(name, out var legacy) && legacy != null)
                legacy.IsEnabled = true;
        }

        public void RenameNodeFlags(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || oldName == newName) return;
            bool enabled = IsNodeEnabled(oldName);
            SetNodeEnabled(oldName, true);
            SetNodeEnabled(newName, enabled);
        }
    }
}
