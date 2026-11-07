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
        private readonly TextBlock _error = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
        public QueueItemEditWindow(QueueItem item)
        {
            _draft = System.Text.Json.JsonSerializer.Deserialize<QueueItemDTO>(System.Text.Json.JsonSerializer.Serialize(item.ToDTO()));
            Title = "Edit queued " + item.RenderKind.ToLowerInvariant();
            Width = 560;
            Height = 760;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.Parse("#262626"));
            int scale = Math.Max(1, _draft.ResolutionScale);
            AddField("Width", Math.Round(_draft.Settings.OutputWidth * 100.0 / scale).ToString(CultureInfo.InvariantCulture));
            AddField("Height", Math.Round(_draft.Settings.OutputHeight * 100.0 / scale).ToString(CultureInfo.InvariantCulture));
            AddField("Scale (%)", scale.ToString());
            AddField("Samples", _draft.Settings.Samples.ToString());
            AddField("Frame start", _draft.Settings.Frame.ToString());
            AddField("Frame end", item.GetFrameNumbers().Last().ToString());
            AddField("Step", Math.Max(1, item.FrameStep).ToString());
            AddField("Frames (override)", item.FrameList);
            AddField("Output path", item.SaveTo);
            AddField("Frame filename", item.FrameFormat);
            var format = new ComboBox { Items = ImageFormats.Formats, SelectedItem = _draft.Settings.RenderFormat };
            AddControl("Format", format);
            var engine = new ComboBox { Items = Enum.GetValues(typeof(EngineType)), SelectedItem = _draft.Settings.Engine };
            AddControl("Engine", engine);
            AddField("Denoiser", _draft.Settings.Denoiser);
            AddField("Scene", _draft.Settings.Scene);
            AddField("Camera", _draft.Settings.Camera);
            var encode = new CheckBox { Content = "Encode MP4 when done", IsChecked = _draft.EncodeMp4, IsVisible = item.IsAnimation };
            _form.Children.Add(encode);
            AddField("MP4 FPS", Math.Max(1, _draft.EncodingFPS).ToString());
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
                    _draft.Settings.Denoiser = _fields["Denoiser"].Text ?? "";
                    _draft.Settings.Scene = _fields["Scene"].Text ?? "";
                    _draft.Settings.Camera = _fields["Camera"].Text ?? "";
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
        private void AddField(string label, string text)
        {
            var field = new TextBox { Text = text ?? "" };
            _fields.Add(label, field);
            AddControl(label, field);
        }
        private void AddControl(string label, Control control)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*") };
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            _form.Children.Add(row);
        }
        private int Number(string field, int minimum, int maximum)
        {
            if (!int.TryParse(_fields[field].Text, out int value) || value < minimum || value > maximum)
                throw new FormatException($"{field} must be an integer from {minimum} to {maximum}.");
            return value;
        }
    }
}
