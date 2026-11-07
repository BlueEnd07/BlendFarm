using System;

namespace LogicReinc.BlendFarm.Objects
{
    /// <summary>
    /// Pure sizing rules for the three-column render window: the side columns are pixel widths (splitters write
    /// pixel values), so they must be clamped whenever the window is smaller than their sum, otherwise the
    /// right-hand render controls end up clipped.
    /// </summary>
    public static class WindowColumnLayout
    {
        public const double LeftMin = 300;
        public const double RightMin = 390;
        /// <summary>The centre (preview) column never gets less than this.</summary>
        public const double CenterMin = 280;
        /// <summary>The two column splitters, 6px each.</summary>
        public const double Splitters = 12;

        /// <summary>
        /// Shrinks the side columns (proportionally to how far each is above its minimum) until the centre
        /// column keeps <see cref="CenterMin"/>. Never grows a column and never goes below the minimums.
        /// </summary>
        public static (double Left, double Right) Clamp(double left, double right, double windowWidth)
        {
            left = Math.Max(LeftMin, left);
            right = Math.Max(RightMin, right);
            double room = Math.Max(LeftMin + RightMin, windowWidth - Splitters - CenterMin);
            double excess = left + right - room;
            if (excess <= 0) return (left, right);
            double shrinkLeft = left - LeftMin, shrinkRight = right - RightMin;
            double total = shrinkLeft + shrinkRight;
            if (total <= 0) return (left, right);
            double take = Math.Min(excess, total);
            return (left - take * shrinkLeft / total, right - take * shrinkRight / total);
        }
    }
}
