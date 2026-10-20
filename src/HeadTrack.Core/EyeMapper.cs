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

        public PoseSource Source { get; set; } = PoseSource.Absolute;

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

        /// <summary>The nearest the virtual eye may come, as a fraction of the neutral distance.</summary>
        public double MinDepthRatio { get; set; } = 0.625;

        /// <summary>The farthest the virtual eye may go, as a multiple of the neutral distance.</summary>
        public double MaxDepthRatio { get; set; } = 1.6;

        public bool InvertX { get; set; }

        public bool InvertY { get; set; }

        public bool InvertZ { get; set; }

        /// <summary>How long centring averages the head pose (s).</summary>
        public double CentreCaptureSeconds { get; set; } = 0.5;

        /// <summary>How long the view takes to glide to a new centre (s).</summary>
        public double CentreBlendSeconds { get; set; } = 0.4;
    }

    /// <summary>
    /// Turns the filtered tracker eye (mm, display frame) into the virtual
    /// eye that drives the window geometry, in the same frame: +x right, +y up,
    /// +z towards the user, origin at the screen centre.
    /// </summary>
    public sealed class EyeMapper
    {
        /// <summary>Below this presence a frame does not count towards a centre.</summary>
        private const double CapturePresence = 0.99;

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

        /// <summary>True once a centre (rest pose) exists; a relative source has one from the start.</summary>
        public bool HasReference => m_HasReference || m_Settings.Source == PoseSource.Relative;

        public bool IsCapturing => m_Capturing;

        /// <summary>The eye (mm) a packet stands for, before any centring.</summary>
        public Vec3 SourceEye(OpenTrackPose pose)
        {
            double x = m_Settings.InvertX ? -pose.X : pose.X;
            double y = m_Settings.InvertY ? -pose.Y : pose.Y;
            double z = m_Settings.InvertZ ? -pose.Z : pose.Z;
            return m_Settings.Source == PoseSource.Absolute
                ? new Vec3(-10.0 * x, 10.0 * y, 10.0 * z)
                : new Vec3(-10.0 * x, 10.0 * y, m_Settings.RelativeDistanceMm + 10.0 * z);
        }

        /// <summary>Starts averaging the head pose; when done, the view glides to the new centre.</summary>
        public void RequestCentre(double now)
        {
            m_Capturing = true;
            m_CaptureStart = now;
            m_CaptureSum = Vec3.Zero;
            m_CaptureCount = 0;
        }

        /// <summary>
        /// The virtual eye for a frame. <paramref name="z0"/> is the distance
        /// at which the game's own field of view fits the screen, and
        /// <paramref name="neutralEye"/> the eye that reproduces the game's
        /// camera exactly (both from the window geometry).
        /// </summary>
        public Vec3 Map(double now, HeadSignalOutput signal, double z0, Vec3 neutralEye)
        {
            if (!signal.HasEye)
            {
                return neutralEye;
            }
            Vec3 eye = signal.Eye;
            Capture(now, eye, signal.Presence);
            Vec3 reference = CurrentReference(now);

            Vec3 mapped;
            Vec3 rest;
            if (m_Settings.Mode == EyeMode.Centred)
            {
                rest = neutralEye;
                if (!HasReference)
                {
                    mapped = neutralEye;
                }
                else
                {
                    double g = m_Settings.Gain;
                    double zn = reference.Z > 1.0 ? reference.Z : m_Settings.RelativeDistanceMm;
                    mapped = new Vec3(
                        neutralEye.X + g * (eye.X - reference.X),
                        neutralEye.Y + g * (eye.Y - reference.Y),
                        z0 * (1.0 + m_Settings.DepthGain * (eye.Z - zn) / zn));
                }
            }
            else
            {
                if (HasReference)
                {
                    rest = reference;
                    mapped = new Vec3(
                        reference.X + m_Settings.Gain * (eye.X - reference.X),
                        reference.Y + m_Settings.Gain * (eye.Y - reference.Y),
                        reference.Z + m_Settings.DepthGain * (eye.Z - reference.Z));
                }
                else
                {
                    rest = neutralEye;
                    mapped = eye;
                }
            }

            Vec3 blended = rest + (mapped - rest) * Clamp01(signal.Presence);
            return Limit(blended, z0, neutralEye);
        }

        private void Capture(double now, Vec3 eye, double presence)
        {
            if (!m_Capturing)
            {
                return;
            }
            if (presence >= CapturePresence)
            {
                m_CaptureSum += eye;
                m_CaptureCount++;
            }
            if (now - m_CaptureStart < m_Settings.CentreCaptureSeconds || m_CaptureCount == 0)
            {
                return;
            }
            Vec3 centre = m_CaptureSum / m_CaptureCount;
            m_Capturing = false;
            if (HasReference)
            {
                m_BlendFrom = CurrentReference(now);
                m_Blending = true;
                m_BlendStart = now;
            }
            m_Reference = centre;
            m_HasReference = true;
        }

        private Vec3 CurrentReference(double now)
        {
            Vec3 target = m_HasReference ? m_Reference : new Vec3(0, 0, m_Settings.RelativeDistanceMm);
            if (!m_Blending)
            {
                return target;
            }
            double progress = (now - m_BlendStart) / m_Settings.CentreBlendSeconds;
            if (progress >= 1)
            {
                m_Blending = false;
                return target;
            }
            double s = progress <= 0 ? 0 : progress * progress * (3 - 2 * progress);
            return m_BlendFrom + (target - m_BlendFrom) * s;
        }

        /// <summary>
        /// Keeps the eye inside the limits: identity for moderate offsets,
        /// then a smooth approach to the limit, so the view never stops dead.
        /// </summary>
        private Vec3 Limit(Vec3 eye, double z0, Vec3 neutralEye)
        {
            double width = m_Settings.ScreenWidthMm;
            double height = m_Settings.ScreenHeightMm;
            double sx = SoftLimit((eye.X - neutralEye.X) / width, m_Settings.MaxShiftX);
            double sy = SoftLimit((eye.Y - neutralEye.Y) / height, m_Settings.MaxShiftY);
            double ratio = eye.Z / z0;
            double logRatio = ratio > 0 ? Math.Log(ratio) : Math.Log(m_Settings.MinDepthRatio);
            double limitedLog = logRatio >= 0
                ? SoftLimit(logRatio, Math.Log(m_Settings.MaxDepthRatio))
                : -SoftLimit(-logRatio, -Math.Log(m_Settings.MinDepthRatio));
            return new Vec3(neutralEye.X + sx * width, neutralEye.Y + sy * height, z0 * Math.Exp(limitedLog));
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
