using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LogicReinc.BlendFarm.Objects
{
    /// <summary>
    /// Non-blocking, human-readable warnings for the File Format (animation) and Output Name (still)
    /// fields. Pure logic so it can be unit-tested without the UI.
    /// </summary>
    public static class OutputNameValidator
    {
        private static readonly string[] KnownTokens =
            { "scene", "camera", "engine", "samples", "width", "height", "date", "time", "frame" };

        // Always the Windows set, so a name that is fine locally but breaks on another node/share is still flagged.
        private static readonly char[] InvalidChars = new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

        private static readonly string[] ImageExtensions =
            { "png", "jpg", "jpeg", "jp2", "bmp", "tga", "cin", "dpx", "exr", "hdr", "tif", "tiff", "webp" };

        /// <summary>Warnings for the animation frame file format (e.g. "frame_####.png").</summary>
        /// <param name="expectedExtension">Extension of the selected output format without dot ("png"), or null if unknown.</param>
        public static List<string> ValidateAnimationFormat(string format, string expectedExtension)
        {
            var warnings = new List<string>();
            if (string.IsNullOrWhiteSpace(format))
            {
                warnings.Add("File format is empty. Use something like frame_####.png.");
                return warnings;
            }

            // {frame} also yields a different name per frame, so it counts as a frame number.
            bool hasFrameNumber = format.Contains("#") ||
                Regex.IsMatch(format, "\\{frame\\}", RegexOptions.IgnoreCase);
            if (!hasFrameNumber)
                warnings.Add("No '#' in the file format: every frame gets the same name and overwrites the previous one. Add # for the frame number.");

            AddCommonWarnings(format, expectedExtension, requireExtension: true, warnings);
            return warnings;
        }

        /// <summary>Warnings for the still output name template (e.g. "{scene}_###").</summary>
        public static List<string> ValidateOutputName(string template, string expectedExtension)
        {
            var warnings = new List<string>();
            // Empty is fine: the app falls back to its default name.
            if (string.IsNullOrWhiteSpace(template))
                return warnings;
            AddCommonWarnings(template, expectedExtension, requireExtension: false, warnings);
            return warnings;
        }

        public static string Join(IEnumerable<string> warnings) => string.Join(Environment.NewLine, warnings ?? Enumerable.Empty<string>());

        private static void AddCommonWarnings(string text, string expectedExtension, bool requireExtension, List<string> warnings)
        {
            // Strip {tokens} first: they are expanded later and their braces are legal.
            string withoutTokens = Regex.Replace(text, "\\{[^{}]*\\}", "");
            var bad = withoutTokens.Where(c => InvalidChars.Contains(c) || char.IsControl(c)).Distinct().ToList();
            if (bad.Count > 0)
                warnings.Add("Invalid filename character" + (bad.Count > 1 ? "s" : "") + ": " +
                    string.Join(" ", bad.Select(c => char.IsControl(c) ? "(control)" : "'" + c + "'")) +
                    ". Windows can't save a file with this name.");

            var unknown = Regex.Matches(text, "\\{([^{}]*)\\}").Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Where(name => !KnownTokens.Contains(name, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (unknown.Count > 0)
                warnings.Add("Unknown token " + string.Join(", ", unknown.Select(u => "{" + u + "}")) +
                    " will be kept as written. Known: " + string.Join(" ", KnownTokens.Select(t => "{" + t + "}")) + ".");

            string actualExtension = GetImageExtension(text);
            if (!string.IsNullOrEmpty(expectedExtension))
            {
                string expected = Normalize(expectedExtension);
                if (actualExtension != null)
                {
                    if (Normalize(actualExtension) != expected)
                        warnings.Add($"Extension .{actualExtension} doesn't match the selected output format (.{expectedExtension}). The file would contain {expectedExtension.ToUpperInvariant()} data.");
                }
                else if (requireExtension)
                {
                    warnings.Add($"No file extension: add .{expectedExtension} so the frames open correctly.");
                }
            }
        }

        /// <summary>Returns the trailing image extension (without dot) or null if the name doesn't end in one.</summary>
        public static string GetImageExtension(string name)
        {
            string ext = Path.GetExtension((name ?? "").Trim());
            if (string.IsNullOrEmpty(ext)) return null;
            ext = ext.TrimStart('.');
            return ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase) ? ext : null;
        }

        private static string Normalize(string ext)
        {
            ext = (ext ?? "").TrimStart('.').ToLowerInvariant();
            return ext switch { "jpeg" => "jpg", "tiff" => "tif", _ => ext };
        }
    }
}
