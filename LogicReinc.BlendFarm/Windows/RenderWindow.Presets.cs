using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Objects;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Windows
{
    // Render settings presets (built-in + user presets persisted in BlendFarmSettings) and
    // the non-blocking warnings under the File Format / Output Name fields.
    public partial class RenderWindow
    {
        private ComboBox _presetSelect;
        private Button _presetDelete;
        private readonly ObservableCollection<RenderPreset> _presetItems = new ObservableCollection<RenderPreset>();
        //Samples each project had before a preset first changed them (so "Final" can restore them)
        private readonly Dictionary<OpenBlenderProject, int?> _presetBaselineSamples = new Dictionary<OpenBlenderProject, int?>();
        private OpenBlenderProject _presetProject;
        private bool _applyingPreset;
        private bool _presetPickedWhileOpen;

        private void InitializePresets()
        {
            _presetSelect = this.FindControl<ComboBox>("presetSelect");
            _presetDelete = this.FindControl<Button>("presetDelete");
            if (_presetSelect == null)
                return;

            _presetSelect.Items = _presetItems;
            ReloadPresets(null);

            _presetSelect.SelectionChanged += (_, __) =>
            {
                if (_applyingPreset)
                    return;
                UpdatePresetButtons();
                if (_presetSelect.SelectedItem is RenderPreset preset)
                {
                    _presetPickedWhileOpen = true;
                    ApplyPreset(preset);
                }
            };
            //Picking the already-selected preset again re-applies it (SelectionChanged doesn't fire for that)
            _presetSelect.GetObservable(ComboBox.IsDropDownOpenProperty).Subscribe(open =>
            {
                if (open)
                    _presetPickedWhileOpen = false;
                else if (!_presetPickedWhileOpen && !_applyingPreset && _presetSelect.SelectedItem is RenderPreset preset)
                    ApplyPreset(preset);
            });
            //A different project tab has its own values: forget which preset was chosen
            this.GetObservable(CurrentProjectProperty).Subscribe(project =>
            {
                if (project == _presetProject)
                    return;
                _presetProject = project;
                _applyingPreset = true;
                try { _presetSelect.SelectedItem = null; }
                finally { _applyingPreset = false; }
                UpdatePresetButtons();
            });
        }

        private void ReloadPresets(string selectName)
        {
            _applyingPreset = true;
            try
            {
                var settings = BlendFarmSettings.Instance;
                settings.UserPresets ??= new List<RenderPreset>();
                _presetItems.Clear();
                foreach (RenderPreset preset in RenderPreset.Combine(settings.UserPresets))
                    _presetItems.Add(preset);
                _presetSelect.SelectedItem = selectName == null ? null
                    : _presetItems.FirstOrDefault(p => string.Equals(p.Name, selectName, StringComparison.OrdinalIgnoreCase));
            }
            finally { _applyingPreset = false; }
            UpdatePresetButtons();
        }

        private void UpdatePresetButtons()
        {
            if (_presetDelete != null)
                _presetDelete.IsEnabled = _presetSelect?.SelectedItem is RenderPreset preset && !preset.BuiltIn;
        }

        public void ApplyPreset(RenderPreset preset)
        {
            OpenBlenderProject project = CurrentProject;
            if (project == null || preset == null)
                return;
            _presetBaselineSamples.TryGetValue(project, out int? baseline);
            string[] changed = preset.ApplyTo(project, ref baseline);
            _presetBaselineSamples[project] = baseline;

            if (!string.IsNullOrEmpty(preset.Strategy) && Enum.TryParse(preset.Strategy, out RenderStrategy strategy) && _selectStrategy != null)
                _selectStrategy.SelectedItem = strategy;

            project.TriggerPropertyChange(changed);
            RefreshCurrentProject();
            AddActivityLog($"Applied preset \"{preset.Name}\"");
        }

        public async void SavePresetClicked(object sender, RoutedEventArgs e)
        {
            OpenBlenderProject project = CurrentProject;
            if (project == null)
                return;
            string suggestion = _presetSelect.SelectedItem is RenderPreset selected && !selected.BuiltIn ? selected.Name : "My preset";
            string name = await TextPromptWindow.Show(this, "Save preset",
                "Name for the preset (saves resolution, scale, samples, denoiser, engine, output format, strategy and chunk size). An existing name is replaced.", suggestion);
            if (name == null)
                return;
            if (RenderPreset.IsReservedName(name))
            {
                await MessageWindow.Show(this, "Preset name", "That name belongs to a built-in preset. Pick another name.");
                return;
            }

            RenderPreset preset = RenderPreset.Capture(name, project, _selectStrategy?.SelectedItem?.ToString());
            var settings = BlendFarmSettings.Instance;
            settings.UserPresets ??= new List<RenderPreset>();
            if (!RenderPreset.Upsert(settings.UserPresets, preset))
                return;
            settings.TrySave();
            ReloadPresets(preset.Name);
            AddActivityLog($"Saved preset \"{preset.Name}\"", ActivityLogType.Success);
        }

        public async void DeletePresetClicked(object sender, RoutedEventArgs e)
        {
            if (!(_presetSelect.SelectedItem is RenderPreset preset) || preset.BuiltIn)
                return;
            if (!await YesNoWindow.ShowOnUIThread(this, "Delete preset", $"Delete the preset \"{preset.Name}\"?"))
                return;
            var settings = BlendFarmSettings.Instance;
            if (RenderPreset.Remove(settings.UserPresets, preset.Name))
                settings.TrySave();
            ReloadPresets(null);
            AddActivityLog($"Deleted preset \"{preset.Name}\"");
        }

        #region Output name warnings

        private TextBlock _animationFormatWarning;
        private TextBlock _outputNameWarning;

        private void InitializeOutputWarnings()
        {
            _animationFormatWarning = this.FindControl<TextBlock>("animationFormatWarning");
            _outputNameWarning = this.FindControl<TextBlock>("outputNameWarning");
            if (_animationFormatWarning == null || _outputNameWarning == null)
                return;
            _inputAnimationFileFormat?.GetObservable(TextBox.TextProperty).Subscribe(_ => UpdateOutputWarnings());
            _inputOutputTemplate?.GetObservable(TextBox.TextProperty).Subscribe(_ => UpdateOutputWarnings());
            _selectOutputType?.GetObservable(SelectingItemsControl.SelectedItemProperty).Subscribe(_ => UpdateOutputWarnings());
            UpdateOutputWarnings();
        }

        private void UpdateOutputWarnings()
        {
            if (_animationFormatWarning == null || _outputNameWarning == null)
                return;
            if (CurrentProject == null)
            {
                SetWarning(_animationFormatWarning, null);
                SetWarning(_outputNameWarning, null);
                return;
            }
            string format = _selectOutputType?.SelectedItem?.ToString();
            string extension = format == null ? null : Client.ImageTypes.ImageFormats.GetExtension(format);
            SetWarning(_animationFormatWarning, OutputNameValidator.ValidateAnimationFormat(_inputAnimationFileFormat?.Text, extension));
            SetWarning(_outputNameWarning, OutputNameValidator.ValidateOutputName(_inputOutputTemplate?.Text, extension));
        }

        private static void SetWarning(TextBlock block, List<string> warnings)
        {
            string text = warnings == null || warnings.Count == 0 ? "" : OutputNameValidator.Join(warnings.Select(w => "⚠ " + w));
            block.Text = text;
            block.IsVisible = text.Length > 0;
        }

        #endregion
    }
}
