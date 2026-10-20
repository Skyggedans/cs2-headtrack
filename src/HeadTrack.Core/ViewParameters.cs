using System;

namespace HeadTrack.Core
{
    /// <summary>
    /// The numbers that shape the view, as one value: the mapping's signed
    /// gains (an inverted axis is a negative gain), the viewing distance, the
    /// screen, the limits and the window depth. Either what the settings ask
    /// for, or, while a change glides in, a point between the old and the new.
    /// </summary>
    public readonly struct ViewParameters : IEquatable<ViewParameters>
    {
        public ViewParameters(Vec3 gain, double viewingDistanceMm, double screenWidthMm, double screenHeightMm,
            double maxShiftX, double maxShiftY, double minDepthRatio, double maxDepthRatio, double windowDepthFraction)
        {
            Gain = gain;
            ViewingDistanceMm = viewingDistanceMm;
            ScreenWidthMm = screenWidthMm;
            ScreenHeightMm = screenHeightMm;
            MaxShiftX = maxShiftX;
            MaxShiftY = maxShiftY;
            MinDepthRatio = minDepthRatio;
            MaxDepthRatio = maxDepthRatio;
            WindowDepthFraction = windowDepthFraction;
        }

        /// <summary>Gains on x, y (the gain) and z (the depth gain), negative where the axis is inverted.</summary>
        public Vec3 Gain { get; }

        /// <summary>The distance a relative pose is an offset from (mm).</summary>
        public double ViewingDistanceMm { get; }

        public double ScreenWidthMm { get; }

        public double ScreenHeightMm { get; }

        public double MaxShiftX { get; }

        public double MaxShiftY { get; }

        public double MinDepthRatio { get; }

        public double MaxDepthRatio { get; }

        /// <summary>The window's depth as a fraction of the aimed point's depth (k).</summary>
        public double WindowDepthFraction { get; }

        /// <summary>The larger sideways gain, as the filter's speed scale: an inverted axis is as fast as the other.</summary>
        public double LateralViewGain => Math.Max(Math.Abs(Gain.X), Math.Abs(Gain.Y));

        public static ViewParameters From(HeadTrackSettings settings) => From(settings.Mapper, settings.WindowDepthFraction);

        public static ViewParameters From(MapperSettings mapper, double windowDepthFraction = 1.0) => new ViewParameters(
            new Vec3(mapper.InvertX ? -mapper.Gain : mapper.Gain, mapper.InvertY ? -mapper.Gain : mapper.Gain,
                mapper.InvertZ ? -mapper.DepthGain : mapper.DepthGain),
            mapper.RelativeDistanceMm, mapper.ScreenWidthMm, mapper.ScreenHeightMm, mapper.MaxShiftX, mapper.MaxShiftY,
            mapper.MinDepthRatio, mapper.MaxDepthRatio, windowDepthFraction);

        /// <summary>
        /// These parameters amplifying the head no more than
        /// <paramref name="maxGain"/> times on any axis (signs kept): the
        /// "g ≤ 1 while a construction tool is active" option. A new cap glides
        /// in like any other change.
        /// </summary>
        public ViewParameters Capped(double maxGain) => !(maxGain < double.PositiveInfinity) ? this : new ViewParameters(
            new Vec3(Cap(Gain.X, maxGain), Cap(Gain.Y, maxGain), Cap(Gain.Z, maxGain)), ViewingDistanceMm, ScreenWidthMm,
            ScreenHeightMm, MaxShiftX, MaxShiftY, MinDepthRatio, MaxDepthRatio, WindowDepthFraction);

        /// <summary>Component by component from <paramref name="a"/> (t = 0) to <paramref name="b"/> (t = 1).</summary>
        public static ViewParameters Lerp(in ViewParameters a, in ViewParameters b, double t) => new ViewParameters(
            a.Gain + (b.Gain - a.Gain) * t,
            Mix(a.ViewingDistanceMm, b.ViewingDistanceMm, t), Mix(a.ScreenWidthMm, b.ScreenWidthMm, t),
            Mix(a.ScreenHeightMm, b.ScreenHeightMm, t), Mix(a.MaxShiftX, b.MaxShiftX, t), Mix(a.MaxShiftY, b.MaxShiftY, t),
            Mix(a.MinDepthRatio, b.MinDepthRatio, t), Mix(a.MaxDepthRatio, b.MaxDepthRatio, t),
            Mix(a.WindowDepthFraction, b.WindowDepthFraction, t));

        /// <summary>Bit for bit: any change of a setting counts.</summary>
        public bool Equals(ViewParameters other) =>
            Same(Gain.X, other.Gain.X) && Same(Gain.Y, other.Gain.Y) && Same(Gain.Z, other.Gain.Z)
            && Same(ViewingDistanceMm, other.ViewingDistanceMm) && Same(ScreenWidthMm, other.ScreenWidthMm)
            && Same(ScreenHeightMm, other.ScreenHeightMm) && Same(MaxShiftX, other.MaxShiftX) && Same(MaxShiftY, other.MaxShiftY)
            && Same(MinDepthRatio, other.MinDepthRatio) && Same(MaxDepthRatio, other.MaxDepthRatio)
            && Same(WindowDepthFraction, other.WindowDepthFraction);

        public override bool Equals(object? obj) => obj is ViewParameters other && Equals(other);

        public override int GetHashCode() => Gain.X.GetHashCode() ^ ScreenHeightMm.GetHashCode() ^ WindowDepthFraction.GetHashCode();

        private static double Mix(double a, double b, double t) => a + (b - a) * t;

        private static double Cap(double gain, double maxGain) => Math.Abs(gain) <= maxGain ? gain : Math.Sign(gain) * Math.Max(0, maxGain);

        private static bool Same(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
    }

    /// <summary>
    /// Follows the view parameters the settings ask for: each change glides
    /// in along a smoothstep from wherever the parameters stand, so no new
    /// gain, depth, screen or window depth ever jumps the camera; once there
    /// the value is the setting exactly. A change during a glide starts a new
    /// glide from the current point.
    /// </summary>
    public sealed class ViewParameterGlide
    {
        private bool m_HasValue;
        private ViewParameters m_From;
        private ViewParameters m_Target;
        private ViewParameters m_Value;
        private double m_Start;

        public ViewParameters Value => m_Value;

        public bool Gliding { get; private set; }

        /// <summary>
        /// The parameters for a frame at <paramref name="now"/>. With
        /// <paramref name="snap"/> (nothing shows: the effect's weight is
        /// zero) or no glide time, the target applies at once.
        /// </summary>
        public ViewParameters Follow(double now, in ViewParameters target, double seconds, bool snap)
        {
            if (!m_HasValue || snap || !(seconds > 0))
            {
                m_HasValue = true;
                m_From = target;
                m_Target = target;
                m_Value = target;
                m_Start = now;
                Gliding = false;
                return m_Value;
            }
            if (!target.Equals(m_Target))
            {
                m_From = m_Value;
                m_Target = target;
                m_Start = now;
                Gliding = true;
            }
            if (Gliding)
            {
                double progress = (now - m_Start) / seconds;
                Gliding = progress < 1;
                m_Value = Gliding ? ViewParameters.Lerp(m_From, m_Target, Ease.Smoothstep(progress)) : m_Target;
            }
            return m_Value;
        }
    }
}
