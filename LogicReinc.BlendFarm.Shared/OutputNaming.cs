using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LogicReinc.BlendFarm.Shared
{
    /// <summary>
    /// Values available when expanding an output-filename template.
    /// </summary>
    public struct OutputNameContext
    {
        public string Scene;
        public string Camera;
        public string Engine;
        public string Format;
        public int Samples;
        public int Width;
        public int Height;
        public int Frame;
        public int StartFrame;
        public int EndFrame;
        public DateTime Timestamp;
    }

    /// <summary>
    /// Pure (Avalonia-free, netstandard2.0) output-filename templating.
    /// Kept in Shared so it is unit-testable without the desktop assembly.
    /// </summary>
    public static class OutputNaming
    {
        /// <summary>
        /// Replaces the first run of '#' characters with the zero-padded frame number.
        /// This is the original FormatAnimationFrameFileName behaviour, extracted verbatim
        /// so existing callers keep producing identical output.
        /// </summary>
        public static string ApplyFrameHashes(string format, int frame, int startFrame, int endFrame)
        {
            if (string.IsNullOrEmpty(format))
                return frame.ToString();

            Match match = Regex.Match(format, "#+");
            if (!match.Success)
                return format;

            int widestFrame = Math.Max(Math.Abs(startFrame), Math.Abs(endFrame));
            int width = match.Length > 1 ? match.Length : Math.Max(3, widestFrame.ToString().Length);
            //Pad the digits only, so -5 becomes -005 rather than 0-5
            string frameText = Math.Abs((long)frame).ToString().PadLeft(width, '0');
            if (frame < 0)
                frameText = "-" + frameText;

            return format.Substring(0, match.Index) + frameText + format.Substring(match.Index + match.Length);
        }

        /// <summary>
        /// Expands the supported tokens in <paramref name="template"/>, then applies the '#'
        /// frame-number padding last so plain "#" / "#.png" templates behave exactly as before.
        /// Supported tokens (case-insensitive): {scene} {camera} {engine} {samples} {width}
        /// {height} {date} {time} {frame}. Token values are sanitized of invalid filename chars.
        /// </summary>
        public static string FormatOutputFileName(string template, OutputNameContext ctx)
        {
            if (string.IsNullOrEmpty(template))
                return ctx.Frame.ToString();

            string s = template;
            s = ReplaceToken(s, "scene", Sanitize(ctx.Scene));
            s = ReplaceToken(s, "camera", Sanitize(ctx.Camera));
            s = ReplaceToken(s, "engine", Sanitize(ctx.Engine));
            s = ReplaceToken(s, "samples", ctx.Samples.ToString());
            s = ReplaceToken(s, "width", ctx.Width.ToString());
            s = ReplaceToken(s, "height", ctx.Height.ToString());
            s = ReplaceToken(s, "date", ctx.Timestamp == default(DateTime) ? "" : ctx.Timestamp.ToString("yyyy-MM-dd"));
            s = ReplaceToken(s, "time", ctx.Timestamp == default(DateTime) ? "" : ctx.Timestamp.ToString("HHmmss"));
            s = ReplaceToken(s, "frame", ctx.Frame.ToString());

            return ApplyFrameHashes(s, ctx.Frame, ctx.StartFrame, ctx.EndFrame);
        }

        /// <summary>
        /// Validates a template by stripping all {tokens} and '#' then checking the remainder
        /// for characters that are illegal in a filename.
        /// </summary>
        public static bool TryValidateTemplate(string template, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(template))
            {
                error = "Template is empty.";
                return false;
            }

            string stripped = Regex.Replace(template, "\\{[a-zA-Z]+\\}", "", RegexOptions.IgnoreCase);
            stripped = stripped.Replace("#", "");

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                if (stripped.IndexOf(c) >= 0)
                {
                    error = "Template contains an invalid filename character: '" + c + "'";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Frames in [start, end] whose output file already exists (and isn't empty) in directory.
        /// Used to resume an interrupted animation. Templates with {date}/{time} never match a previous run.
        /// </summary>
        public static List<int> FindExistingFrames(string directory, int start, int end, Func<int, string> fileNameForFrame)
        {
            List<int> existing = new List<int>();
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return existing;
            for (int frame = start; frame <= end; frame++)
            {
                FileInfo info = new FileInfo(Path.Combine(directory, fileNameForFrame(frame)));
                if (info.Exists && info.Length > 0)
                    existing.Add(frame);
            }
            return existing;
        }

        private static readonly string[] ImageExtensions = new string[]
        {
            ".png", ".jpg", ".jpeg", ".jp2", ".bmp", ".tga", ".cin", ".dpx", ".exr", ".hdr", ".tif", ".tiff", ".webp"
        };

        /// <summary>
        /// True if the name ends in a known image extension. Path.GetExtension alone treats
        /// any dot as an extension, e.g. a scene named "Shot.v2".
        /// </summary>
        public static bool HasImageExtension(string name)
        {
            string ext = Path.GetExtension(name ?? "");
            return Array.Exists(ImageExtensions, x => string.Equals(x, ext, StringComparison.OrdinalIgnoreCase));
        }

        private static string ReplaceToken(string input, string token, string value)
        {
            //Evaluator so '$' in a value isn't treated as a regex substitution
            return Regex.Replace(input, "\\{" + token + "\\}", m => value ?? "", RegexOptions.IgnoreCase);
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c.ToString(), "");
            //'#' in a token value would be taken as frame padding
            value = value.Replace("#", "");
            return value;
        }
    }
}
