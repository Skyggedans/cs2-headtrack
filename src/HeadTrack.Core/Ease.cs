namespace HeadTrack.Core
{
    /// <summary>The one easing curve of the core: fades, ramps and glides all follow it.</summary>
    internal static class Ease
    {
        /// <summary>3p² − 2p³ on [0, 1], clamped outside: zero slope at both ends, steepest (1.5) in the middle.</summary>
        public static double Smoothstep(double p)
        {
            p = p < 0 ? 0 : p > 1 ? 1 : p;
            return p * p * (3 - 2 * p);
        }

        public static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;

        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
