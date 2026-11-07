using LogicReinc.BlendFarm.Objects;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm
{
    /// <summary>
    /// Posts render-completion notifications to a user-configured webhook.
    /// Payload is Discord-compatible (content + a single embed) but is plain
    /// JSON any endpoint can consume.
    /// </summary>
    public static class WebhookNotifier
    {
        //One shared client; creating an HttpClient per call exhausts sockets.
        private static readonly HttpClient _client = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        /// <summary>
        /// Fire-and-forget friendly: never throws. Returns true if the POST
        /// completed with a success status code.
        /// </summary>
        public static async Task<bool> PostAsync(string url, RenderCompletionInfo info)
        {
            if (string.IsNullOrWhiteSpace(url) || info == null)
                return false;

            try
            {
                string json = BuildPayload(info);
                using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage response = await _client.PostAsync(url, content))
                {
                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                //Swallow: a bad URL or offline endpoint must never disturb rendering.
                Console.WriteLine($"Webhook notification failed: {ex.Message}");
                return false;
            }
        }

        private static string BuildPayload(RenderCompletionInfo info)
        {
            string frames = info.IsAnimation
                ? $"{info.FrameStart}-{info.FrameEnd} ({info.FrameCount} frames)"
                : info.FrameStart.ToString();

            string status = info.Success ? "Completed" : "Failed";
            string title = info.Success
                ? $"Render complete: {info.SceneName}"
                : $"Render failed: {info.SceneName}";

            List<object> fields = new List<object>
            {
                new { name = "Scene", value = string.IsNullOrEmpty(info.SceneName) ? "-" : info.SceneName, inline = true },
                new { name = info.IsAnimation ? "Frames" : "Frame", value = frames, inline = true },
                new { name = "Elapsed", value = FormatDuration(info.Elapsed), inline = true },
                new { name = "Nodes", value = info.NodeCount.ToString(), inline = true },
                new { name = "Resolution", value = $"{info.Width}x{info.Height}", inline = true },
                new { name = "Samples", value = info.Samples.ToString(), inline = true },
                new { name = "Status", value = info.Success ? status : $"{status}: {info.EngineOrError}", inline = false }
            };

            var payload = new
            {
                content = title,
                embeds = new[]
                {
                    new
                    {
                        title = title,
                        color = info.Success ? 3066993 /*green*/ : 15158332 /*red*/,
                        fields = fields.ToArray()
                    }
                }
            };

            return JsonSerializer.Serialize(payload);
        }

        private static string FormatDuration(TimeSpan span)
        {
            if (span.TotalHours >= 1)
                return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
            return $"{span.Minutes}:{span.Seconds:00}";
        }
    }
}
