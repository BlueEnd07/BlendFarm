using System;
using System.Runtime.InteropServices;

namespace LogicReinc.BlendFarm
{
    /// <summary>
    /// Keeps the machine from going to sleep while a render is running. Windows only
    /// (SetThreadExecutionState); a no-op everywhere else.
    /// SetThreadExecutionState is per thread, so Set must always be called from the same thread (the UI thread).
    /// </summary>
    public static class PowerManagement
    {
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);

        public static bool IsSupported => OperatingSystem.IsWindows();

        /// <summary>True while this process is asking the OS to stay awake.</summary>
        public static bool KeepingAwake { get; private set; }

        /// <summary>Pure decision: should the PC be kept awake right now?</summary>
        public static bool ShouldKeepAwake(bool settingEnabled, bool rendering, bool queueing, bool liveRenderRunning) =>
            settingEnabled && (rendering || queueing || liveRenderRunning);

        /// <summary>Requests (or releases) the wake lock. Returns true if the state changed.</summary>
        public static bool SetKeepAwake(bool keepAwake)
        {
            if (keepAwake == KeepingAwake)
                return false;
            if (IsSupported)
            {
                try
                {
                    SetThreadExecutionState(keepAwake ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Keep-awake unavailable: " + ex.Message);
                    return false;
                }
            }
            KeepingAwake = keepAwake;
            return true;
        }
    }
}
