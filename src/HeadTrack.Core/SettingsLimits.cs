using System;
using System.Collections.Generic;
using System.Globalization;

namespace HeadTrack.Core
{
    /// <summary>
    /// The range of every setting. The options page's sliders take their
    /// bounds from here (constants, so attributes can use them) and
    /// <see cref="HeadTrackSettings.Validate"/> keeps a hand-edited or old
    /// settings file inside the same ranges.
    /// </summary>
    public static class SettingsLimits
    {
        /// <summary>Below 1 the effect is subtle; 3-5 orbits a building; at 8 the head reaches the shift limit within about 2 cm.</summary>
        public const double GainMin = 0.25;

        public const double GainMax = 8.0;

        /// <summary>0 keeps the eye at its distance; up to 3 a lean stays clear of the screen (the depth gain scales the log of the distance).</summary>
        public const double DepthGainMin = 0.0;

        public const double DepthGainMax = 3.0;

        /// <summary>A window much nearer than the aimed point turns small head moves into extreme frusta.</summary>
        public const double WindowDepthMin = 0.25;

        public const double WindowDepthMax = 2.0;

        public const double ViewingDistanceMinMm = 250.0;

        public const double ViewingDistanceMaxMm = 1500.0;

        public const double ScreenWidthMinMm = 100.0;

        public const double ScreenWidthMaxMm = 3000.0;

        public const double ScreenHeightMinMm = 60.0;

        public const double ScreenHeightMaxMm = 2000.0;

        public const double MaxShiftMin = 0.1;

        public const double MaxShiftMax = 1.5;

        public const double MinDepthRatioMin = 0.2;

        public const double MinDepthRatioMax = 0.95;

        public const double MaxDepthRatioMin = 1.05;

        public const double MaxDepthRatioMax = 5.0;

        /// <summary>The longest fade, ramp or glide (s). Those may be 0: at once.</summary>
        public const double SecondsMax = 5.0;

        /// <summary>A frame longer than this counts as a pause; it must exceed a slow frame (about 7 fps), or every frame is a cut.</summary>
        public const double GapSecondsMin = 0.15;

        public const double GapSecondsMax = 10.0;

        /// <summary>The face counts as lost after this silence at the earliest; 0 would never let it be present.</summary>
        public const double PresenceTimeoutMin = 0.05;

        public const double PresenceTimeoutMax = 10.0;

        public const int PortMin = 1024;

        public const int PortMax = 65535;

        /// <summary>A UDP port typed into the options page: a whole number from <see cref="PortMin"/> to <see cref="PortMax"/>.</summary>
        public static bool TryParsePort(string? text, out int port, out string? error)
        {
            error = null;
            if (!int.TryParse((text ?? string.Empty).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port)
                || port < PortMin || port > PortMax)
            {
                port = 0;
                error = $"a port is a number from {PortMin} to {PortMax}";
                return false;
            }
            return true;
        }

        /// <summary><paramref name="value"/> inside [min, max], or <paramref name="fallback"/> when it is not a finite number; any change is reported.</summary>
        internal static double Check(List<string> corrections, string name, double value, double fallback, double min, double max)
        {
            double result = !Ease.IsFinite(value) ? fallback : Ease.Clamp(value, min, max);
            if (BitConverter.DoubleToInt64Bits(result) != BitConverter.DoubleToInt64Bits(value))
            {
                corrections.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1} -> {2}", name, value, result));
            }
            return result;
        }

        internal static int Check(List<string> corrections, string name, int value, int min, int max)
        {
            int result = value < min ? min : value > max ? max : value;
            if (result != value)
            {
                corrections.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1} -> {2}", name, value, result));
            }
            return result;
        }

        /// <summary>A 1€ parameter set with a positive (or infinite: off) cutoff, a non-negative β and a positive derivative cutoff.</summary>
        internal static OneEuroParameters Check(List<string> corrections, string name, OneEuroParameters value, OneEuroParameters fallback)
        {
            double minCutoff = double.IsPositiveInfinity(value.MinCutoff) ? value.MinCutoff
                : Check(corrections, name + ".min_cutoff", value.MinCutoff, fallback.MinCutoff, 1e-3, 100);
            double beta = Check(corrections, name + ".beta", value.Beta, fallback.Beta, 0, 10);
            double dCutoff = Check(corrections, name + ".d_cutoff", value.DCutoff, fallback.DCutoff, 1e-2, 100);
            return new OneEuroParameters(minCutoff, beta, dCutoff);
        }
    }

    /// <summary>The options page's one smoothing control, 0-100 %, as the filter's parameters.</summary>
    public static class Smoothing
    {
        public const int Min = 0;

        public const int Max = 100;

        /// <summary>The filter the core was tuned with.</summary>
        public const int Default = 50;

        /// <summary>
        /// x and y: the cutoff at rest halves every 25 % — about 2 Hz at 1 %,
        /// 0.5 Hz at 50 %, 0.125 Hz at 100 % — and 0 % is off. β and the
        /// derivative cutoff stay as tuned, so a deliberate move keeps its latency.
        /// </summary>
        public static OneEuroParameters Lateral(int percent) =>
            percent <= Min ? OneEuroParameters.Off : new OneEuroParameters(0.5 * Factor(percent), 0.02, 2.0);

        /// <summary>z, slower in the same proportion as the defaults (0.3 Hz at 50 %).</summary>
        public static OneEuroParameters Depth(int percent) =>
            percent <= Min ? OneEuroParameters.Off : new OneEuroParameters(0.3 * Factor(percent), 0.01, 1.5);

        private static double Factor(int percent) => Math.Pow(2.0, (Default - Math.Min(percent, Max)) / 25.0);
    }
}
