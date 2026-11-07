using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Client.ImageTypes;
using LogicReinc.BlendFarm.Objects;
using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LogicReinc.BlendFarm.Windows
{
    public class QueueItemEditWindow : Window
    {
        private readonly Dictionary<string, TextBox> _fields = new Dictionary<string, TextBox>();
        private readonly StackPanel _form = new StackPanel { Spacing = 8, Margin = new Thickness(18) };
        private readonly QueueItemDTO _draft;
        private readonly TextBlock _error = new TextBlock { Foreground = ThemeBrushes.Error, TextWrapping = TextWrapping.Wrap };
        public QueueItemEditWindow(QueueItem item)
        {
            _draft = System.Text.Json.JsonSerializer.Deserialize<QueueItemDTO>(System.Text.Json.JsonSerializer.Serialize(item.ToDTO()));
            Title = "Edit queued " + item.RenderKind.ToLowerInvariant();
            Width = 560;
            Height = 760;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = ThemeBrushes.Window;
            int scale = Math.Max(1, _draft.ResolutionScale);
            AddField("Width", Math.Round(_draft.Settings.OutputWidth * 100.0 / scale).ToString(CultureInfo.InvariantCulture));
            AddField("Height", Math.Round(_draft.Settings.OutputHeight * 100.0 / scale).ToString(CultureInfo.InvariantCulture));
            AddField("Scale (%)", scale.ToString());
            AddField("Samples", _draft.Settings.Samples.ToString());
            bool animation = item.IsAnimation;
            AddField("Frame start", _draft.Settings.Frame.ToString(), animation ? null : "Frame");
            AddField("Frame end", item.GetFrameNumbers().Last().ToString(), null, animation);
            AddField("Step", Math.Max(1, item.FrameStep).ToString(), null, animation);
            AddField("Frames (override)", item.FrameList, null, animation);
            AddField("Output path", item.SaveTo);
            AddField("Frame filename", item.FrameFormat, null, animation);
            var format = new ComboBox { Items = ImageFormats.Formats, SelectedItem = _draft.Settings.RenderFormat };
            AddControl("Format", format);
            var engine = new ComboBox { Items = Enum.GetValues(typeof(EngineType)), SelectedItem = _draft.Settings.Engine };
            AddControl("Engine", engine);
            var denoiser = new ComboBox { Items = WithCurrent(DenoiserChoices, _draft.Settings.Denoiser), SelectedItem = string.IsNullOrEmpty(_draft.Settings.Denoiser) ? "Inherit" : _draft.Settings.Denoiser };
            AddControl("Denoiser", denoiser);
            var scene = ProjectChoiceBox(item.Project?.ScenesAvailable, _draft.Settings.Scene);
            AddControl("Scene", scene);
            var camera = ProjectChoiceBox(item.Project?.CamerasAvailable, _draft.Settings.Camera);
            AddControl("Camera", camera);
            var encode = new CheckBox { Content = "Encode MP4 when done", IsChecked = _draft.EncodeMp4, IsVisible = animation };
            _form.Children.Add(encode);
            AddField("MP4 FPS", Math.Max(1, _draft.EncodingFPS).ToString(), null, animation);
            _form.Children.Add(_error);
            var save = new Button { Content = "Apply changes", HorizontalAlignment = HorizontalAlignment.Right };
            save.Click += (_, __) =>
            {
                try
                {
                    int width = Number("Width", 1, 65536);
                    int height = Number("Height", 1, 65536);
                    int newScale = Number("Scale (%)", 1, 1000);
                    _draft.Settings.OutputWidth = Math.Max(1, checked((int)Math.Round(width * newScale / 100.0)));
                    _draft.Settings.OutputHeight = Math.Max(1, checked((int)Math.Round(height * newScale / 100.0)));
                    _draft.ResolutionScale = newScale;
                    _draft.Settings.Samples = Number("Samples", 1, int.MaxValue);
                    int start = Number("Frame start", 0, int.MaxValue);
                    int end = Number("Frame end", 0, int.MaxValue);
                    _draft.FrameStep = Number("Step", 1, int.MaxValue);
                    _draft.FrameList = _fields["Frames (override)"].Text;
                    _draft.FrameNumbers = item.IsAnimation ? AnimationFrameSelection.Parse(start, end, _draft.FrameStep, _draft.FrameList).ToArray() : null;
                    _draft.Settings.Frame = _draft.FrameNumbers?.First() ?? start;
                    _draft.Frames = _draft.FrameNumbers?.Length ?? 1;
                    _draft.SaveTo = _fields["Output path"].Text;
                    if (item.IsAnimation && string.IsNullOrWhiteSpace(_draft.SaveTo)) throw new FormatException("Choose an output folder.");
                    if (!string.IsNullOrWhiteSpace(_draft.SaveTo)) Path.GetFullPath(_draft.SaveTo);
                    _draft.FrameFormat = _fields["Frame filename"].Text;
                    if (item.IsAnimation && (string.IsNullOrWhiteSpace(_draft.FrameFormat) || !_draft.FrameFormat.Contains("#")))
                        throw new FormatException("Frame filenames must include # (for example render_####.png).");
                    if (item.IsAnimation && !OutputNaming.TryValidateTemplate(_draft.FrameFormat, out string templateError)) throw new FormatException(templateError);
                    _draft.Settings.RenderFormat = (string)format.SelectedItem ?? "PNG";
                    if (item.IsAnimation) _draft.FrameFormat = Path.ChangeExtension(_draft.FrameFormat, ImageFormats.GetExtension(_draft.Settings.RenderFormat));
                    _draft.Settings.Engine = (EngineType)engine.SelectedItem;
                    _draft.Settings.Denoiser = denoiser.SelectedItem as string ?? "Inherit";
                    _draft.Settings.Scene = ChoiceValue(scene);
                    _draft.Settings.Camera = ChoiceValue(camera);
                    _draft.EncodeMp4 = encode.IsChecked == true;
                    _draft.EncodingFPS = Number("MP4 FPS", 1, 240);
                    _draft.OutputTimestamp = null;
                    _draft.OutputFrameStart = null;
                    _draft.OutputFrameEnd = null;
                    Close(_draft);
                }
                catch (Exception ex) { _error.Text = ex.Message; }
            };
            _form.Children.Add(save);
            Content = new ScrollViewer { Content = _form };
        }
        private static readonly string[] DenoiserChoices = { "Inherit", "None", "NLM", "OPTIX", "OPENIMAGEDENOISE" };
        private const string DefaultChoice = "(project default)";
        private static string[] WithCurrent(string[] choices, string current)
            => string.IsNullOrEmpty(current) || choices.Contains(current) ? choices : choices.Concat(new[] { current }).ToArray();
        //Scenes / cameras read from the project when it has been inspected, otherwise only the item's current value is offered
        private static ComboBox ProjectChoiceBox(IEnumerable<string> available, string current)
        {
            var items = new List<string> { DefaultChoice };
            if (available != null) items.AddRange(available.Where(name => !string.IsNullOrEmpty(name)));
            if (!string.IsNullOrEmpty(current) && !items.Contains(current)) items.Add(current);
            return new ComboBox { Items = items, SelectedItem = string.IsNullOrEmpty(current) ? DefaultChoice : current };
        }
        private static string ChoiceValue(ComboBox box)
            => box.SelectedItem is string value && value != DefaultChoice ? value : "";
        private void AddField(string label, string text, string displayLabel = null, bool visible = true)
        {
            var field = new TextBox { Text = text ?? "" };
            _fields.Add(label, field);
            AddControl(displayLabel ?? label, field).IsVisible = visible;
        }
        private Grid AddControl(string label, Control control)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*") };
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            _form.Children.Add(row);
            return row;
        }
        private int Number(string field, int minimum, int maximum)
        {
            if (!int.TryParse(_fields[field].Text, out int value) || value < minimum || value > maximum)
                throw new FormatException($"{field} must be an integer from {minimum} to {maximum}.");
            return value;
        }
    }
}
