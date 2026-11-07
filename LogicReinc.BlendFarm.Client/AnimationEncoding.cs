using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LogicReinc.BlendFarm.Client
{
    public static class AnimationEncoding
    {
        public static string BuildManifest(IEnumerable<string> framePaths)
        {
            string[] paths = framePaths.ToArray();
            if (paths.Length == 0) throw new ArgumentException("No frames to encode.");
            return string.Join("\n", paths.Select(path =>
            {
                if (string.IsNullOrWhiteSpace(path) || path.Contains("\n") || path.Contains("\r"))
                    throw new ArgumentException("Invalid frame path.");
                return "file '" + Path.GetFullPath(path).Replace('\\', '/').Replace("'", "'\\''") + "'";
            })) + "\n";
        }
        public static IReadOnlyList<string> BuildArguments(string manifestPath, string outputPath, int fps)
        {
            if (fps < 1 || fps > 240) throw new ArgumentOutOfRangeException(nameof(fps), "FPS must be between 1 and 240.");
            return new[] { "-hide_banner", "-y", "-f", "concat", "-safe", "0", "-r", fps.ToString(CultureInfo.InvariantCulture),
                "-i", manifestPath, "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "18",
                "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-movflags", "+faststart", outputPath };
        }
    }
}
