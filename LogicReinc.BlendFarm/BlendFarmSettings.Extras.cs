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
    }
}
