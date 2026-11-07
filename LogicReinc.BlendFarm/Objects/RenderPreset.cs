using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace LogicReinc.BlendFarm.Objects
{
    /// <summary>
    /// A named bundle of render settings. Every field is optional: null means "leave the project's value alone".
    /// User presets capture all fields; built-in presets only override what they name.
    /// </summary>
    public class RenderPreset
    {
        public const string PreviewName = "Preview (50%, 16 samples)";
        public const string FinalName = "Final (100%, project samples)";

        public string Name { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public int? RenderScale { get; set; }
        public int? Samples { get; set; }
        public string Denoiser { get; set; }
        public string Engine { get; set; }
        public string OutputFormat { get; set; }
        public string Strategy { get; set; }
        public int? ChunkSize { get; set; }

        /// <summary>Restore the samples the project had before a preset changed them (the "Final" preset).</summary>
        public bool RestoreProjectSamples { get; set; }

        [JsonIgnore]
        public bool BuiltIn { get; set; }

        public override string ToString() => Name;

        public static IReadOnlyList<RenderPreset> BuiltIns { get; } = new List<RenderPreset>
        {
            new RenderPreset { Name = PreviewName, RenderScale = 50, Samples = 16, BuiltIn = true },
            new RenderPreset { Name = FinalName, RenderScale = 100, RestoreProjectSamples = true, BuiltIn = true }
        };

        /// <summary>Built-in presets followed by the user's, de-duplicated by name (built-ins win).</summary>
        public static List<RenderPreset> Combine(IEnumerable<RenderPreset> userPresets)
        {
            var all = new List<RenderPreset>(BuiltIns);
            foreach (var p in userPresets ?? Enumerable.Empty<RenderPreset>())
                if (p != null && !string.IsNullOrWhiteSpace(p.Name) && !IsReservedName(p.Name) &&
                    !all.Any(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
                    all.Add(p);
            return all;
        }

        public static bool IsReservedName(string name) =>
            BuiltIns.Any(b => string.Equals(b.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Captures every preset field from the project (strategy comes from the UI, not the project).</summary>
        public static RenderPreset Capture(string name, OpenBlenderProject project, string strategy)
        {
            return new RenderPreset
            {
                Name = name?.Trim(),
                Width = project.RenderWidth,
                Height = project.RenderHeight,
                RenderScale = project.RenderScale,
                Samples = project.Samples,
                Denoiser = project.Denoiser,
                Engine = project.Engine.ToString(),
                OutputFormat = project.RenderFormat,
                Strategy = strategy,
                ChunkSize = project.ChunkSize
            };
        }

        /// <summary>
        /// Applies the preset to the project. <paramref name="baselineSamples"/> remembers the samples the project had
        /// before the first preset overrode them, so the "Final" preset can bring them back.
        /// Returns the names of the project properties that changed (for change notifications).
        /// </summary>
        public string[] ApplyTo(OpenBlenderProject project, ref int? baselineSamples)
        {
            var changed = new List<string>();
            if (Width.HasValue && Width.Value > 0) { project.RenderWidth = Width.Value; changed.Add(nameof(project.RenderWidth)); }
            if (Height.HasValue && Height.Value > 0) { project.RenderHeight = Height.Value; changed.Add(nameof(project.RenderHeight)); }
            if (RenderScale.HasValue && RenderScale.Value > 0) { project.RenderScale = RenderScale.Value; changed.Add(nameof(project.RenderScale)); }
            if (Samples.HasValue && Samples.Value > 0)
            {
                if (baselineSamples == null) baselineSamples = project.Samples;
                project.Samples = Samples.Value;
                changed.Add(nameof(project.Samples));
            }
            else if (RestoreProjectSamples && baselineSamples.HasValue)
            {
                project.Samples = baselineSamples.Value;
                baselineSamples = null;
                changed.Add(nameof(project.Samples));
            }
            if (!string.IsNullOrEmpty(Denoiser)) { project.Denoiser = Denoiser; changed.Add(nameof(project.Denoiser)); }
            if (!string.IsNullOrEmpty(Engine) && Enum.TryParse(Engine, out EngineType engine)) { project.Engine = engine; changed.Add(nameof(project.Engine)); }
            if (!string.IsNullOrEmpty(OutputFormat)) { project.RenderFormat = OutputFormat; changed.Add(nameof(project.RenderFormat)); }
            if (ChunkSize.HasValue && ChunkSize.Value > 0) { project.ChunkSize = ChunkSize.Value; changed.Add(nameof(project.ChunkSize)); }
            return changed.ToArray();
        }

        /// <summary>Adds or replaces (case-insensitive name match) a user preset. Returns false for reserved or empty names.</summary>
        public static bool Upsert(List<RenderPreset> userPresets, RenderPreset preset)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.Name) || IsReservedName(preset.Name))
                return false;
            preset.Name = preset.Name.Trim();
            int index = userPresets.FindIndex(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) userPresets[index] = preset; else userPresets.Add(preset);
            return true;
        }

        public static bool Remove(List<RenderPreset> userPresets, string name) =>
            userPresets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
    }
}
