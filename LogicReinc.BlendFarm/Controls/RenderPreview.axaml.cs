using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Visuals.Media.Imaging;
using System;

namespace LogicReinc.BlendFarm.Controls
{
    /// <summary>
    /// Image viewer for renders: wheel zoom around the cursor, drag to pan, double-click / Fit to fit,
    /// 1:1 for 100% pixels, checkerboard behind transparency and an optional before/after compare slider.
    /// </summary>
    public partial class RenderPreview : UserControl
    {
        public static readonly StyledProperty<IImage> SourceProperty =
            AvaloniaProperty.Register<RenderPreview, IImage>(nameof(Source));
        /// <summary>Image shown on the left ("before") side while comparing.</summary>
        public static readonly StyledProperty<IImage> CompareSourceProperty =
            AvaloniaProperty.Register<RenderPreview, IImage>(nameof(CompareSource));
        public static readonly StyledProperty<bool> IsCompareEnabledProperty =
            AvaloniaProperty.Register<RenderPreview, bool>(nameof(IsCompareEnabled));
        public static readonly StyledProperty<bool> ShowHistoryButtonProperty =
            AvaloniaProperty.Register<RenderPreview, bool>(nameof(ShowHistoryButton), true);
        /// <summary>Adds "Delete: clear the image" to the tooltip, only for hosts that handle the Delete key.</summary>
        public static readonly StyledProperty<bool> ShowClearHintProperty =
            AvaloniaProperty.Register<RenderPreview, bool>(nameof(ShowClearHint), false);

        public IImage Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
        public IImage CompareSource { get => GetValue(CompareSourceProperty); set => SetValue(CompareSourceProperty, value); }
        public bool IsCompareEnabled { get => GetValue(IsCompareEnabledProperty); set => SetValue(IsCompareEnabledProperty, value); }
        public bool ShowHistoryButton { get => GetValue(ShowHistoryButtonProperty); set => SetValue(ShowHistoryButtonProperty, value); }
        public bool ShowClearHint { get => GetValue(ShowClearHintProperty); set => SetValue(ShowClearHintProperty, value); }

        /// <summary>Raised when the History button is pressed.</summary>
        public event Action HistoryRequested;

        /// <summary>True while compare mode is actually drawn (enabled and there is a previous image).</summary>
        public bool IsComparing => IsCompareEnabled && CompareSource != null && Source != null;

        private ViewTransform _t = new ViewTransform(1, 0, 0);
        private bool _fit = true;
        private double _split = 0.5;
        private Size _lastBounds;

        private bool _panning, _draggingSlider;
        private Point _panStart;
        private ViewTransform _panOrigin;

        private TextBlock _zoomText;
        private ToggleButton _compareButton;
        private Button _historyButton, _fitButton, _actualButton;
        private Border _toolbar;
        private bool _syncingCompare;

        private static WriteableBitmap _checkerImage;

        static RenderPreview()
        {
            AffectsRender<RenderPreview>(SourceProperty, CompareSourceProperty, IsCompareEnabledProperty);
            SourceProperty.Changed.AddClassHandler<RenderPreview>((c, e) => c.OnSourceChanged(e.OldValue as IImage, e.NewValue as IImage));
            CompareSourceProperty.Changed.AddClassHandler<RenderPreview>((c, e) => c.UpdateCompareUi());
            IsCompareEnabledProperty.Changed.AddClassHandler<RenderPreview>((c, e) => c.UpdateCompareUi());
            ShowHistoryButtonProperty.Changed.AddClassHandler<RenderPreview>((c, e) => c.UpdateCompareUi());
            ShowClearHintProperty.Changed.AddClassHandler<RenderPreview>((c, e) => c.UpdateZoomTip());
        }

        public RenderPreview()
        {
            AvaloniaXamlLoader.Load(this);
            _zoomText = this.FindControl<TextBlock>("zoomText");
            _compareButton = this.FindControl<ToggleButton>("compareButton");
            _historyButton = this.FindControl<Button>("historyButton");
            _toolbar = this.FindControl<Border>("toolbar");
            _fitButton = this.FindControl<Button>("fitButton");
            _actualButton = this.FindControl<Button>("actualButton");
            _toolbar.PointerPressed += (s, e) => e.Handled = true;
            _toolbar.PointerWheelChanged += (s, e) => e.Handled = true;
            UpdateZoomTip();
            _fitButton.Click += (s, e) => { FitToView(); Focus(); };
            _actualButton.Click += (s, e) => { ActualSize(); Focus(); };
            _historyButton.Click += (s, e) => HistoryRequested?.Invoke();
            _compareButton.Click += (s, e) =>
            {
                if (_syncingCompare) return;
                IsCompareEnabled = _compareButton.IsChecked == true;
                Focus();
            };
            this.GetObservable(BoundsProperty).Subscribe(OnBoundsChanged);
            UpdateCompareUi();
        }

        // ---------------------------------------------------------------- state

        private double Scaling => VisualRoot?.RenderScaling ?? 1;

        private static Size PixelSizeOf(IImage image)
        {
            if (image == null) return default;
            if (image is Bitmap bmp)
            {
                try { return new Size(bmp.PixelSize.Width, bmp.PixelSize.Height); }
                catch (ObjectDisposedException) { return default; }
            }
            return image.Size;
        }

        private Size ImageSize => PixelSizeOf(Source);

        /// <summary>Current transform, refitted first when in fit mode.</summary>
        private ViewTransform Current()
        {
            Size img = ImageSize;
            if (_fit && img.Width > 0 && img.Height > 0 && Bounds.Width > 0 && Bounds.Height > 0)
                _t = ZoomMath.Fit(Bounds.Width, Bounds.Height, img.Width, img.Height);
            return _t;
        }

        /// <summary>Zoom level in percent of device pixels, as shown in the toolbar.</summary>
        public int ZoomPercent => ZoomMath.Percent(Current().Scale, Scaling);

        private void Refresh()
        {
            if (_zoomText != null)
                _zoomText.Text = Source == null ? "" : ZoomMath.Percent(Current().Scale, Scaling) + "%";
            InvalidateVisual();
        }

        private void UpdateZoomTip()
        {
            if (_zoomText == null) return;
            ToolTip.SetTip(_zoomText, "Mouse wheel: zoom at the cursor\nDrag: pan\nDouble-click: fit" + (ShowClearHint ? "\nDelete: clear the image" : ""));
        }

        private void OnSourceChanged(IImage oldImage, IImage newImage)
        {
            UpdateCompareUi();
            Size o = PixelSizeOf(oldImage), n = PixelSizeOf(newImage);
            //Tile previews swap the image constantly, only a new/different-sized image resets the view
            if (oldImage == null || newImage == null || o != n)
                _fit = true;
            Refresh();
        }

        private void OnBoundsChanged(Rect bounds)
        {
            Size now = bounds.Size;
            if (!_fit && _lastBounds.Width > 0 && _lastBounds.Height > 0 && now.Width > 0 && now.Height > 0)
            {
                //Keep the view centre fixed while the control resizes
                _t = new ViewTransform(_t.Scale, _t.OffsetX + (now.Width - _lastBounds.Width) / 2, _t.OffsetY + (now.Height - _lastBounds.Height) / 2);
            }
            _lastBounds = now;
            Refresh();
        }

        private void UpdateCompareUi()
        {
            if (_compareButton == null) return;
            bool hasImage = Source != null;
            //Zoom controls need an image, Compare needs both the current and the previous one
            _zoomText.IsVisible = _fitButton.IsVisible = _actualButton.IsVisible = hasImage;
            _compareButton.IsVisible = hasImage && CompareSource != null;
            _syncingCompare = true;
            try { _compareButton.IsChecked = IsCompareEnabled; }
            finally { _syncingCompare = false; }
            _historyButton.IsVisible = ShowHistoryButton;
            _toolbar.IsVisible = hasImage || ShowHistoryButton;
            InvalidateVisual();
        }

        // ---------------------------------------------------------------- commands

        public void FitToView()
        {
            _fit = true;
            Refresh();
        }

        public void ActualSize() => ZoomTo(1.0 / Scaling, Bounds.Width / 2, Bounds.Height / 2);

        private void ZoomTo(double scale, double anchorX, double anchorY)
        {
            Size img = ImageSize;
            if (img.Width <= 0 || img.Height <= 0 || Bounds.Width <= 0) return;
            ViewTransform t = Current();
            double min = ZoomMath.MinScale(Bounds.Width, Bounds.Height, img.Width, img.Height);
            scale = ZoomMath.ClampScale(scale, min, ZoomMath.MaxScale);
            //Anchor on the image itself: a cursor outside it would make the image drift towards an edge
            (anchorX, anchorY) = ZoomMath.ClampAnchorToImage(t, anchorX, anchorY, img.Width, img.Height);
            t = ZoomMath.ZoomAround(t, scale, anchorX, anchorY);
            _t = ZoomMath.ClampOffset(t, Bounds.Width, Bounds.Height, img.Width, img.Height);
            _fit = false;
            Refresh();
        }

        // ---------------------------------------------------------------- input

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            if (Source == null || e.Delta.Y == 0) return;
            Point p = e.GetPosition(this);
            ZoomTo(Current().Scale * ZoomMath.WheelFactor(e.Delta.Y), p.X, p.Y);
            e.Handled = true;
        }

        private Rect ImageRect()
        {
            Size img = ImageSize;
            ViewTransform t = Current();
            return new Rect(t.OffsetX, t.OffsetY, img.Width * t.Scale, img.Height * t.Scale);
        }

        private bool OverSlider(Point p)
        {
            if (!IsComparing) return false;
            Rect r = ImageRect();
            double x = r.X + r.Width * _split;
            return Math.Abs(p.X - x) <= 12 && p.Y >= r.Top - 4 && p.Y <= r.Bottom + 4;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            if (Source == null) return;
            var props = e.GetCurrentPoint(this).Properties;
            Point p = e.GetPosition(this);
            if (props.IsLeftButtonPressed && OverSlider(p))
            {
                _draggingSlider = true;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
            if (props.IsLeftButtonPressed && e.ClickCount >= 2)
            {
                FitToView();
                e.Handled = true;
                return;
            }
            if (props.IsLeftButtonPressed || props.IsMiddleButtonPressed)
            {
                _panning = true;
                _panStart = p;
                _panOrigin = Current();
                Cursor = new Cursor(StandardCursorType.SizeAll);
                e.Pointer.Capture(this);
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            Point p = e.GetPosition(this);
            if (_draggingSlider)
            {
                Rect r = ImageRect();
                _split = r.Width <= 0 ? 0.5 : Math.Min(1, Math.Max(0, (p.X - r.X) / r.Width));
                InvalidateVisual();
                return;
            }
            if (_panning)
            {
                Size img = ImageSize;
                var t = new ViewTransform(_panOrigin.Scale, _panOrigin.OffsetX + (p.X - _panStart.X), _panOrigin.OffsetY + (p.Y - _panStart.Y));
                _t = ZoomMath.ClampOffset(t, Bounds.Width, Bounds.Height, img.Width, img.Height);
                _fit = false;
                Refresh();
                return;
            }
            Cursor = OverSlider(p) ? new Cursor(StandardCursorType.SizeWestEast) : Cursor.Default;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_panning || _draggingSlider)
            {
                _panning = false;
                _draggingSlider = false;
                e.Pointer.Capture(null);
                Cursor = Cursor.Default;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Source == null) return;
            double cx = Bounds.Width / 2, cy = Bounds.Height / 2;
            switch (e.Key)
            {
                case Key.F:
                case Key.Home:
                    FitToView(); e.Handled = true; break;
                case Key.D1:
                case Key.NumPad1:
                    ActualSize(); e.Handled = true; break;
                case Key.OemPlus:
                case Key.Add:
                    ZoomTo(Current().Scale * 1.25, cx, cy); e.Handled = true; break;
                case Key.OemMinus:
                case Key.Subtract:
                    ZoomTo(Current().Scale / 1.25, cx, cy); e.Handled = true; break;
            }
        }

        // ---------------------------------------------------------------- drawing

        private static WriteableBitmap CheckerImage()
        {
            if (_checkerImage != null) return _checkerImage;
            const int square = 8;
            var bmp = new WriteableBitmap(new PixelSize(square * 2, square * 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var fb = bmp.Lock())
            {
                var row = new int[square * 2];
                for (int y = 0; y < square * 2; y++)
                {
                    for (int x = 0; x < square * 2; x++)
                        row[x] = ((x / square + y / square) & 1) == 0 ? unchecked((int)0xFF4A4A4A) : unchecked((int)0xFF383838);
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
                }
            }
            return _checkerImage = bmp;
        }

        private static double Mod(double value, double m) => ((value % m) + m) % m;

        private void DrawChecker(DrawingContext ctx, Rect visible, Point origin)
        {
            var brush = new ImageBrush(CheckerImage())
            {
                TileMode = TileMode.Tile,
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                //The tile origin is relative to the filled rectangle
                DestinationRect = new RelativeRect(Mod(origin.X - visible.X, 16), Mod(origin.Y - visible.Y, 16), 16, 16, RelativeUnit.Absolute)
            };
            ctx.FillRectangle(brush, visible);
        }

        private void DrawLabel(DrawingContext ctx, string text, Point at)
        {
            var ft = new FormattedText { Text = text, Typeface = Typeface.Default, FontSize = 11 };
            var box = new Rect(at.X, at.Y, ft.Bounds.Width + 12, ft.Bounds.Height + 6);
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(190, 20, 20, 20)), box, 4);
            ctx.DrawText(Brushes.White, new Point(at.X + 6, at.Y + 3), ft);
        }

        public override void Render(DrawingContext ctx)
        {
            base.Render(ctx);
            IImage img = Source;
            if (img == null) return;
            Size px = PixelSizeOf(img);
            if (px.Width <= 0 || px.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;

            ViewTransform t = Current();
            double scaling = Scaling;
            double x = t.OffsetX, y = t.OffsetY;
            if (t.Scale * scaling >= 1)
            {
                //Pixel-aligned origin keeps 1:1 sharp
                x = Math.Round(x * scaling) / scaling;
                y = Math.Round(y * scaling) / scaling;
            }
            var dest = new Rect(x, y, px.Width * t.Scale, px.Height * t.Scale);
            var view = new Rect(Bounds.Size);
            Rect visible = dest.Intersect(view);
            if (visible.Width <= 0 || visible.Height <= 0) return;

            BitmapInterpolationMode mode = t.Scale * scaling >= 2 ? BitmapInterpolationMode.LowQuality : BitmapInterpolationMode.HighQuality;
            try
            {
                DrawChecker(ctx, visible, dest.TopLeft);
                if (IsComparing)
                {
                    IImage before = CompareSource;
                    Size bpx = PixelSizeOf(before);
                    double splitX = dest.X + dest.Width * _split;
                    if (bpx.Width > 0 && bpx.Height > 0)
                    {
                        using (ctx.PushClip(new Rect(visible.X, visible.Y, Math.Max(0, Math.Min(visible.Right, splitX) - visible.X), visible.Height)))
                            ctx.DrawImage(before, new Rect(0, 0, bpx.Width, bpx.Height), dest, mode);
                    }
                    using (ctx.PushClip(new Rect(Math.Max(visible.X, splitX), visible.Y, Math.Max(0, visible.Right - Math.Max(visible.X, splitX)), visible.Height)))
                        ctx.DrawImage(img, new Rect(0, 0, px.Width, px.Height), dest, mode);

                    DrawSlider(ctx, visible, splitX);
                }
                else
                    ctx.DrawImage(img, new Rect(0, 0, px.Width, px.Height), dest, mode);
            }
            catch (ObjectDisposedException)
            {
                //The previous bitmap was disposed while the view was catching up with the new one
            }
        }

        private void DrawSlider(DrawingContext ctx, Rect visible, double splitX)
        {
            if (splitX >= visible.X - 1 && splitX <= visible.Right + 1)
            {
                ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 4), new Point(splitX, visible.Top), new Point(splitX, visible.Bottom));
                ctx.DrawLine(new Pen(Brushes.White, 2), new Point(splitX, visible.Top), new Point(splitX, visible.Bottom));
                var center = new Point(splitX, (visible.Top + visible.Bottom) / 2);
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(230, 30, 30, 30)), new Pen(Brushes.White, 2), center, 14, 14);
                //Drawn instead of glyphs, not every font has the arrow characters
                var arrows = new StreamGeometry();
                using (StreamGeometryContext g = arrows.Open())
                {
                    g.BeginFigure(new Point(center.X - 10, center.Y), true);
                    g.LineTo(new Point(center.X - 4, center.Y - 5));
                    g.LineTo(new Point(center.X - 4, center.Y + 5));
                    g.EndFigure(true);
                    g.BeginFigure(new Point(center.X + 10, center.Y), true);
                    g.LineTo(new Point(center.X + 4, center.Y - 5));
                    g.LineTo(new Point(center.X + 4, center.Y + 5));
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(Brushes.White, null, arrows);
            }
            if (visible.Left < splitX - 70)
                DrawLabel(ctx, "BEFORE", new Point(visible.Left + 8, visible.Top + 8));
            if (visible.Right > splitX + 70)
                DrawLabel(ctx, "AFTER", new Point(Math.Min(visible.Right - 58, Math.Max(splitX + 8, visible.Left + 8)), visible.Top + 8));
        }
    }
}
