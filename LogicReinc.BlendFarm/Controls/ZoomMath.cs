using System;

namespace LogicReinc.BlendFarm.Controls
{
    /// <summary>
    /// Maps image pixels to view coordinates: view = image * Scale + Offset.
    /// </summary>
    public readonly struct ViewTransform
    {
        public double Scale { get; }
        public double OffsetX { get; }
        public double OffsetY { get; }

        public ViewTransform(double scale, double offsetX, double offsetY)
        {
            Scale = scale;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        public (double X, double Y) ToView(double imageX, double imageY) => (imageX * Scale + OffsetX, imageY * Scale + OffsetY);
        public (double X, double Y) ToImage(double viewX, double viewY) => ((viewX - OffsetX) / Scale, (viewY - OffsetY) / Scale);
    }

    /// <summary>
    /// Pure zoom/pan math for the render preview (no UI types so it can be unit tested).
    /// </summary>
    public static class ZoomMath
    {
        public const double MaxScale = 64;

        /// <summary>Scale at which the whole image fits in the view (images smaller than the view are scaled up, like Stretch=Uniform).</summary>
        public static double FitScale(double viewWidth, double viewHeight, double imageWidth, double imageHeight)
        {
            if (viewWidth <= 0 || viewHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
                return 1;
            return Math.Min(viewWidth / imageWidth, viewHeight / imageHeight);
        }

        /// <summary>Image centred in the view at the given scale.</summary>
        public static ViewTransform Centered(double viewWidth, double viewHeight, double imageWidth, double imageHeight, double scale)
            => new ViewTransform(scale, (viewWidth - imageWidth * scale) / 2, (viewHeight - imageHeight * scale) / 2);

        public static ViewTransform Fit(double viewWidth, double viewHeight, double imageWidth, double imageHeight)
            => Centered(viewWidth, viewHeight, imageWidth, imageHeight, FitScale(viewWidth, viewHeight, imageWidth, imageHeight));

        /// <summary>Lowest allowed scale: well below fit, but never a vanishing image.</summary>
        public static double MinScale(double viewWidth, double viewHeight, double imageWidth, double imageHeight)
            => Math.Max(0.005, Math.Min(FitScale(viewWidth, viewHeight, imageWidth, imageHeight), 1) * 0.25);

        public static double ClampScale(double scale, double min, double max)
        {
            if (double.IsNaN(scale)) return min;
            return Math.Min(Math.Max(scale, min), Math.Max(min, max));
        }

        /// <summary>Changes the scale while the image point under (anchorX, anchorY) stays put.</summary>
        public static ViewTransform ZoomAround(ViewTransform current, double newScale, double anchorX, double anchorY)
        {
            if (current.Scale <= 0 || newScale <= 0)
                return current;
            double ratio = newScale / current.Scale;
            return new ViewTransform(newScale,
                anchorX - (anchorX - current.OffsetX) * ratio,
                anchorY - (anchorY - current.OffsetY) * ratio);
        }

        /// <summary>Scale multiplier for a mouse wheel step (delta in wheel notches, positive = zoom in).</summary>
        public static double WheelFactor(double delta) => Math.Pow(1.15, delta);

        /// <summary>
        /// Keeps at least <paramref name="keepVisible"/> pixels of the image inside the view on both axes
        /// so it can't be panned out of sight.
        /// </summary>
        public static ViewTransform ClampOffset(ViewTransform t, double viewWidth, double viewHeight, double imageWidth, double imageHeight, double keepVisible = 64)
        {
            double w = imageWidth * t.Scale, h = imageHeight * t.Scale;
            double keepX = Math.Min(keepVisible, w), keepY = Math.Min(keepVisible, h);
            double x = Math.Min(Math.Max(t.OffsetX, keepX - w), viewWidth - keepX);
            double y = Math.Min(Math.Max(t.OffsetY, keepY - h), viewHeight - keepY);
            return new ViewTransform(t.Scale, x, y);
        }

        /// <summary>Zoom readout in percent, relative to device pixels (100% = one image pixel per screen pixel).</summary>
        public static int Percent(double scale, double renderScaling) => (int)Math.Round(scale * Math.Max(renderScaling, 0.01) * 100);
    }
}
