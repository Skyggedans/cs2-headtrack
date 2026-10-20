using System;

namespace HeadTrack.Core
{
    /// <summary>How the tracked eye drives the virtual eye.</summary>
    public enum EyeMode
    {
        /// <summary>
        /// The head's pose when centring is the neutral one: it gives exactly
        /// the game's own view; offsets from it are scaled by the gain, and
        /// depth proportionally, so leaning keeps distant things their size.
        /// </summary>
        Centred,

        /// <summary>
        /// A true window: the eye is where the tracker says, relative to the
        /// screen centre, so at rest the view is what a window would show
        /// from where the user sits; the gain amplifies motion around the
        /// captured rest pose.
        /// </summary>
        Window,
    }

    /// <summary>What the six numbers of a packet mean.</summary>
    public enum PoseSource
    {
        /// <summary>tobii-opentrack: the eye in the display frame, cm.</summary>
        Absolute,

        /// <summary>OpenTrack's output: an offset from its own centre, cm; depth comes from the settings.</summary>
        Relative,
    }

    public sealed class MapperSettings
    {
        public EyeMode Mode { get; set; } = EyeMode.Centred;

        /// <summary>
        /// OpenTrack's "UDP over network" output by default: centred by
        /// OpenTrack itself, so its centre is the neutral pose.
        /// </summary>
        public PoseSource Source { get; set; } = PoseSource.Relative;

        /// <summary>Gain on sideways and vertical head motion; 1 is physically right, 3-5 orbits a building.</summary>
        public double Gain { get; set; } = 1.0;

        /// <summary>Gain on motion towards and away from the screen.</summary>
        public double DepthGain { get; set; } = 1.0;

        /// <summary>The viewing distance a relative pose is an offset from (mm).</summary>
        public double RelativeDistanceMm { get; set; } = 600.0;

        public double ScreenWidthMm { get; set; } = 597.0;

        public double ScreenHeightMm { get; set; } = 336.0;

        /// <summary>The largest sideways eye offset, as a fraction of the screen width.</summary>
        public double MaxShiftX { get; set; } = 0.6;

        /// <summary>The largest vertical eye offset, as a fraction of the screen height.</summary>
        public double MaxShiftY { get; set; } = 0.45;

        /// <summary>The nearest the virtual eye may come, as a fraction of the rest distance (the neutral eye's when centred, the rest pose's in window mode).</summary>
        public double MinDepthRatio { get; set; } = 0.625;

        /// <summary>The farthest the virtual eye may go, as a multiple of the rest distance.</summary>
        public double MaxDepthRatio { get; set; } = 1.6;

        /// <summary>Mirror sideways motion around the rest pose (a negative gain).</summary>
        public bool InvertX { get; set; }

        /// <summary>Mirror vertical motion around the rest pose.</summary>
        public bool InvertY { get; set; }

        /// <summary>Mirror motion towards and away from the screen around the rest pose.</summary>
        public bool InvertZ { get; set; }

        /// <summary>How long centring averages the head pose (s).</summary>
        public double CentreCaptureSeconds { get; set; } = 0.5;

        /// <summary>How long the view takes to glide to a new centre (s).</summary>
        public double CentreBlendSeconds { get; set; } = 0.4;
    }

    /// <summary>
    /// Turns the filtered tracker numbers into the virtual eye that drives the
    /// window geometry (mm, display frame: +x right, +y up, +z towards the
    /// user, origin at the screen centre). The filter works on the tracker's
    /// own numbers; what they mean (the source, the viewing distance) and how
    /// they are scaled (gains, inversions) is applied here, per frame, so any
    /// of it can change while the camera runs: the centre is kept in tracker
    /// numbers too and means the same after the change.
    /// </summary>
    public sealed class EyeMapper
    {
        /// <summary>Below this presence a frame does not count towards a centre.</summary>
        private const double CapturePresence = 0.99;

        /// <summary>Distances below this (mm) count as this, so a lean through the screen never takes the log of zero.</summary>
        private const double MinDistanceMm = 1.0;

        private readonly MapperSettings m_Settings;
        private bool m_Capturing;
        private double m_CaptureStart;
        private Vec3 m_CaptureSum;
        private int m_CaptureCount;
        private bool m_HasReference;
        private Vec3 m_Reference;
        private bool m_Blending;
        private Vec3 m_BlendFrom;
        private double m_BlendStart;

        public EyeMapper(MapperSettings settings)
        {
            m_Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public MapperSettings Settings => m_Settings;

        /// <summary>True once a centre (rest pose) exists; a relative source has one from the start: OpenTrack's own.</summary>
        public bool HasReference => m_HasReference || m_Settings.Source == PoseSource.Relative;

        public bool IsCapturing => m_Capturing;

        /// <summary>The eye (mm) a packet stands for under the current source and viewing distance, before any centring.</summary>
        public Vec3 SourceEye(OpenTrackPose pose) => Interpret(pose.EyeMillimetres, m_Settings.RelativeDistanceMm);

        /// <summary>Starts averaging the head pose; when done, the view glides to the new centre.</summary>
        public void RequestCentre(double now)
        {
            m_Capturing = true;
            m_CaptureStart = now;
            m_CaptureSum = Vec3.Zero;
            m_CaptureCount = 0;
        }

        /// <summary>
        /// Drops the captured centre and any capture or glide in progress: the
        /// numbers of a new source mean something else. A relative source
        /// falls back on OpenTrack's centre; an absolute one needs a new capture.
        /// </summary>
        public void ForgetCentre()
        {
            m_HasReference = false;
            m_Capturing = false;
            m_Blending = false;
        }

        /// <summary>
        /// The virtual eye for a frame with the parameters the settings give
        /// now. <paramref name="z0"/> is the distance at which the game's own
        /// field of view fits the screen, and <paramref name="neutralEye"/> the
        /// eye that reproduces the game's camera exactly.
        /// </summary>
        public Vec3 Map(double now, HeadSignalOutput signal, double z0, Vec3 neutralEye) =>
            Map(now, signal, z0, neutralEye, ViewParameters.From(m_Settings));

        /// <summary>The virtual eye for a frame with <paramref name="view"/>: the parameters as they glide.</summary>
        public Vec3 Map(double now, HeadSignalOutput signal, double z0, Vec3 neutralEye, in ViewParameters view)
        {
            if (!signal.HasEye)
            {
                return neutralEye;
            }
            Capture(now, signal.Eye, signal.Presence, signal.Settling);
            double distance = view.ViewingDistanceMm;
            Vec3 eye = Interpret(signal.Eye, distance);
            Vec3 reference = Interpret(CurrentReference(now), distance);
            Vec3 gain = view.Gain;

            Vec3 mapped;
            Vec3 rest;
            if (m_Settings.Mode == EyeMode.Centred)
            {
                rest = neutralEye;
                mapped = !HasReference
                    ? neutralEye
                    : new Vec3(
                        neutralEye.X + gain.X * (eye.X - reference.X),
                        neutralEye.Y + gain.Y * (eye.Y - reference.Y),
                        z0 * DepthRatio(eye.Z, reference.Z, gain.Z));
            }
            else if (HasReference)
            {
                rest = reference;
                mapped = new Vec3(
                    reference.X + gain.X * (eye.X - reference.X),
                    reference.Y + gain.Y * (eye.Y - reference.Y),
                    reference.Z * DepthRatio(eye.Z, reference.Z, gain.Z));
            }
            else
            {
                rest = neutralEye;
                mapped = eye;
            }

            Vec3 blended = rest + (mapped - rest) * Clamp01(signal.Presence);
            return Limit(blended, rest, view);
        }

        /// <summary>The tracker's numbers (mm) as an eye: a relative pose is an offset from the viewing distance.</summary>
        private Vec3 Interpret(Vec3 raw, double distance) =>
            m_Settings.Source == PoseSource.Relative ? new Vec3(raw.X, raw.Y, raw.Z + distance) : raw;

        /// <summary>
        /// (z / zn)^gain: the depth gain scales the log of the distance ratio,
        /// so at gain 1 the eye's distance is in proportion to the head's, and
        /// at any gain a deep lean approaches the screen without reaching or
        /// crossing it.
        /// </summary>
        private static double DepthRatio(double z, double zn, double gain) =>
            Math.Exp(gain * Math.Log(Math.Max(z, MinDistanceMm) / Math.Max(zn, MinDistanceMm)));

        /// <summary>
        /// Averages the head over <see cref="MapperSettings.CentreCaptureSeconds"/>
        /// of frames in which it is steadily there (full presence, not
        /// settling); a frame that is not starts the average over. The
        /// reference then glides to the new centre — the first one from the
        /// eye itself, so the view starts from neutral either way. Everything
        /// here is in tracker numbers.
        /// </summary>
        private void Capture(double now, Vec3 eye, double presence, bool settling)
        {
            if (!m_Capturing)
            {
                return;
            }
            if (presence < CapturePresence || settling)
            {
                m_CaptureSum = Vec3.Zero;
                m_CaptureCount = 0;
                return;
            }
            if (m_CaptureCount == 0)
            {
                m_CaptureStart = now;
            }
            m_CaptureSum += eye;
            m_CaptureCount++;
            if (now - m_CaptureStart < m_Settings.CentreCaptureSeconds)
            {
                return;
            }
            Vec3 centre = m_CaptureSum / m_CaptureCount;
            if (m_Settings.Source == PoseSource.Absolute
                && !(centre.Z >= SettingsLimits.ViewingDistanceMinMm && centre.Z <= SettingsLimits.ViewingDistanceMaxMm))
            {
                // Not an eye in front of the screen: another sender's numbers
                // (OpenTrack's zeros after a switch). Start over.
                m_CaptureSum = Vec3.Zero;
                m_CaptureCount = 0;
                return;
            }
            m_Capturing = false;
            m_BlendFrom = HasReference ? CurrentReference(now) : eye;
            m_Blending = true;
            m_BlendStart = now;
            m_Reference = centre;
            m_HasReference = true;
        }

        /// <summary>The centre in tracker numbers; a relative source without a capture is centred where OpenTrack is (zero).</summary>
        private Vec3 CurrentReference(double now)
        {
            Vec3 target = m_HasReference ? m_Reference : Vec3.Zero;
            if (!m_Blending)
            {
                return target;
            }
            double seconds = m_Settings.CentreBlendSeconds;
            double progress = seconds > 0 ? (now - m_BlendStart) / seconds : 1;
            if (progress >= 1)
            {
                m_Blending = false;
                return target;
            }
            return m_BlendFrom + (target - m_BlendFrom) * Ease.Smoothstep(progress);
        }

        /// <summary>
        /// Keeps the eye inside the limits, measured from the rest pose:
        /// identity for moderate offsets, then a smooth approach to the limit,
        /// so the view never stops dead — and a true window at unit gain shows
        /// the rest pose exactly, wherever the user sits.
        /// </summary>
        private static Vec3 Limit(Vec3 eye, Vec3 rest, in ViewParameters view)
        {
            double width = view.ScreenWidthMm;
            double height = view.ScreenHeightMm;
            double sx = SoftLimit((eye.X - rest.X) / width, view.MaxShiftX);
            double sy = SoftLimit((eye.Y - rest.Y) / height, view.MaxShiftY);
            double logRatio = Math.Log(Math.Max(eye.Z, MinDistanceMm) / Math.Max(rest.Z, MinDistanceMm));
            double limitedLog = logRatio >= 0
                ? SoftLimit(logRatio, Math.Log(view.MaxDepthRatio))
                : -SoftLimit(-logRatio, -Math.Log(view.MinDepthRatio));
            return new Vec3(rest.X + sx * width, rest.Y + sy * height, rest.Z * Math.Exp(limitedLog));
        }

        /// <summary>Identity up to half the limit, then a C1 tanh knee approaching the limit.</summary>
        private static double SoftLimit(double value, double limit)
        {
            double knee = 0.5 * limit;
            double magnitude = Math.Abs(value);
            if (magnitude <= knee)
            {
                return value;
            }
            double room = limit - knee;
            double limited = knee + room * Math.Tanh((magnitude - knee) / room);
            return value < 0 ? -limited : limited;
        }

        private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;
    }
}
