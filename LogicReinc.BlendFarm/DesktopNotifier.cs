using LogicReinc.BlendFarm.Objects;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LogicReinc.BlendFarm
{
    /// <summary>
    /// OS-level completion feedback: a desktop notification and a system sound.
    /// Needs no admin rights and no extra packages (Windows: a toast through the built-in PowerShell,
    /// MessageBeep for the sound). Everything fails silently.
    /// </summary>
    public static class DesktopNotifier
    {
        private const uint MB_ICONHAND = 0x10;      //failure sound
        private const uint MB_ICONASTERISK = 0x40;  //success sound

        [DllImport("user32.dll")]
        private static extern bool MessageBeep(uint type);

        public static string BuildTitle(RenderCompletionInfo info) =>
            info.Success ? (info.IsAnimation ? "Animation complete" : "Render complete") : "Render failed";

        public static string BuildBody(RenderCompletionInfo info)
        {
            string scene = string.IsNullOrWhiteSpace(info.SceneName) ? "BlendFarm" : info.SceneName;
            if (!info.Success)
                return string.IsNullOrWhiteSpace(info.EngineOrError) ? scene : $"{scene}: {info.EngineOrError}";
            string elapsed = info.Elapsed.TotalHours >= 1
                ? $"{(int)info.Elapsed.TotalHours}h {info.Elapsed.Minutes}m {info.Elapsed.Seconds}s"
                : $"{(int)info.Elapsed.TotalMinutes}m {info.Elapsed.Seconds}s";
            string frames = info.IsAnimation ? $", {info.FrameCount} frames" : "";
            return $"{scene} finished in {elapsed}{frames}";
        }

        /// <summary>Pure decision shared by notification and sound.</summary>
        public static bool ShouldNotify(bool featureEnabled, bool onlyWhenUnfocused, bool windowFocused) =>
            featureEnabled && (!onlyWhenUnfocused || !windowFocused);

        public static void Show(string title, string body)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    ShowWindowsToast(title, body);
                else if (OperatingSystem.IsMacOS())
                    Run("osascript", new[] { "-e", "display notification (system attribute \"BF_BODY\") with title (system attribute \"BF_TITLE\")" }, title, body);
                else if (OperatingSystem.IsLinux())
                    Run("notify-send", new[] { title, body }, title, body);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Desktop notification failed: " + ex.Message);
            }
        }

        public static void PlaySound(bool success)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    MessageBeep(success ? MB_ICONASTERISK : MB_ICONHAND);
                else if (OperatingSystem.IsMacOS())
                    Run("afplay", new[] { success ? "/System/Library/Sounds/Glass.aiff" : "/System/Library/Sounds/Basso.aiff" });
                else if (OperatingSystem.IsLinux())
                {
                    string file = success ? "/usr/share/sounds/freedesktop/stereo/complete.oga"
                                          : "/usr/share/sounds/freedesktop/stereo/dialog-error.oga";
                    if (File.Exists(file))
                        Run("paplay", new[] { file });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Completion sound failed: " + ex.Message);
            }
        }

        //Title/body travel through environment variables so they are never parsed as script code.
        private const string WindowsToastScript = @"
$ErrorActionPreference = 'Stop'
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
$xml = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
$text = $xml.GetElementsByTagName('text')
$text.Item(0).AppendChild($xml.CreateTextNode($env:BF_TITLE)) | Out-Null
$text.Item(1).AppendChild($xml.CreateTextNode($env:BF_BODY)) | Out-Null
$toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
$appId = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($appId).Show($toast)
";

        private static void ShowWindowsToast(string title, string body)
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(WindowsToastScript));
            Run("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-EncodedCommand", encoded }, title, body);
        }

        private static void Run(string file, string[] args, string title = null, string body = null)
        {
            var psi = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string a in args)
                psi.ArgumentList.Add(a);
            if (title != null) psi.Environment["BF_TITLE"] = title;
            if (body != null) psi.Environment["BF_BODY"] = body;
            Process process = Process.Start(psi);
            if (process == null) return;
            //Drain output and reap the process without blocking the caller
            process.OutputDataReceived += (s, e) => { };
            process.ErrorDataReceived += (s, e) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.EnableRaisingEvents = true;
            process.Exited += (s, e) => process.Dispose();
        }
    }
}
