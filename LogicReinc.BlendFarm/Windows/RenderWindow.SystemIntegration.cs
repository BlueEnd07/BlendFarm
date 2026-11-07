using Avalonia.Threading;
using LogicReinc.BlendFarm.Objects;
using System;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Windows
{
    // Keep-awake while rendering, desktop notification and completion sound, and their settings.
    public partial class RenderWindow
    {
        private DispatcherTimer _keepAwakeTimer;

        public bool PreventSleepWhileRendering
        {
            get => BlendFarmSettings.Instance.PreventSleepWhileRendering;
            set { BlendFarmSettings.Instance.PreventSleepWhileRendering = value; BlendFarmSettings.Instance.TrySave(); UpdateKeepAwake(); }
        }
        public bool DesktopNotificationEnabled
        {
            get => BlendFarmSettings.Instance.DesktopNotificationEnabled;
            set { BlendFarmSettings.Instance.DesktopNotificationEnabled = value; BlendFarmSettings.Instance.TrySave(); }
        }
        public bool PlayCompletionSound
        {
            get => BlendFarmSettings.Instance.PlayCompletionSound;
            set { BlendFarmSettings.Instance.PlayCompletionSound = value; BlendFarmSettings.Instance.TrySave(); }
        }
        public bool NotifyOnlyWhenUnfocused
        {
            get => BlendFarmSettings.Instance.NotifyOnlyWhenUnfocused;
            set { BlendFarmSettings.Instance.NotifyOnlyWhenUnfocused = value; BlendFarmSettings.Instance.TrySave(); }
        }

        private void InitializeSystemIntegration()
        {
            //Polling the render state is cheaper than threading a hook through every render path
            //(still, animation, queue, benchmark, live render all pass through IsRendering/IsQueueing).
            _keepAwakeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _keepAwakeTimer.Tick += (_, __) => UpdateKeepAwake();
            _keepAwakeTimer.Start();
            Closed += (_, __) =>
            {
                _keepAwakeTimer?.Stop();
                PowerManagement.SetKeepAwake(false);
            };
        }

        private void UpdateKeepAwake()
        {
            bool want = PowerManagement.ShouldKeepAwake(
                BlendFarmSettings.Instance.PreventSleepWhileRendering, IsRendering, IsQueueing, _liveRenderLoopRunning);
            if (PowerManagement.SetKeepAwake(want))
                AddActivityLog(want ? "Keeping this PC awake while rendering" : "Sleep prevention released");
        }

        // Called for every finished or failed render / queue item, next to the in-app toast.
        private void DesktopNotifyRenderCompleted(RenderCompletionInfo info)
        {
            BlendFarmSettings settings = BlendFarmSettings.Instance;
            if (!settings.DesktopNotificationEnabled && !settings.PlayCompletionSound)
                return;
            Dispatcher.UIThread.Post(() =>
            {
                bool focused = IsActive;
                bool notify = DesktopNotifier.ShouldNotify(settings.DesktopNotificationEnabled, settings.NotifyOnlyWhenUnfocused, focused);
                bool sound = DesktopNotifier.ShouldNotify(settings.PlayCompletionSound, settings.NotifyOnlyWhenUnfocused, focused);
                if (!notify && !sound)
                    return;
                string title = DesktopNotifier.BuildTitle(info);
                string body = DesktopNotifier.BuildBody(info);
                _ = Task.Run(() =>
                {
                    if (notify) DesktopNotifier.Show(title, body);
                    if (sound) DesktopNotifier.PlaySound(info.Success);
                });
            });
        }
    }
}
