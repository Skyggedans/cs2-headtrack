using System;
using System.Collections.Generic;

namespace HeadTrack.Core
{
    /// <summary>Everything the head-coupled camera can be tuned with.</summary>
    public sealed class HeadTrackSettings
    {
        public SignalSettings Signal { get; } = new SignalSettings();

        public MapperSettings Mapper { get; } = new MapperSettings();

        /// <summary>The window's depth as a fraction of the aimed point's depth (k).</summary>
        public double WindowDepthFraction { get; set; } = 1.0;

        /// <summary>How long the effect takes to come in or go out when a gate opens or closes (s).</summary>
        public double RampSeconds { get; set; } = 0.4;

        /// <summary>A pause between frames longer than this (s: loading, a menu) counts as a cut, so the effect ramps in again.</summary>
        public double GapSeconds { get; set; } = 0.25;

        /// <summary>An eye offset below this (mm) writes nothing, so the game stays bit-exact.</summary>
        public double MinOffsetMm { get; set; } = 1e-3;

        /// <summary>How many times an offset that gives no view is halved before the frame gives up.</summary>
        public int ShrinkAttempts { get; set; } = 4;

        /// <summary>How long the camera takes to glide back to the head when a freeze ends (s).</summary>
        public double FreezeReleaseSeconds { get; set; } = 0.3;

        /// <summary>
        /// How long a changed setting takes to glide in (s): a gain, depth
        /// gain, window depth, screen, viewing distance, limit or inversion,
        /// and a switch of mode or source. 0 applies at once.
        /// </summary>
        public double GlideSeconds { get; set; } = 0.4;

        /// <summary>
        /// Keeps every setting inside <see cref="SettingsLimits"/>: what is not
        /// a number becomes the default, the rest is clamped; durations that
        /// may be 0 (at once) stay at or above 0, those that must not (the gap,
        /// the presence timeout) are raised. Returns what it changed, for the log.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var c = new List<string>();
            var d = new HeadTrackSettings();
            WindowDepthFraction = SettingsLimits.Check(c, "window_depth", WindowDepthFraction, d.WindowDepthFraction,
                SettingsLimits.WindowDepthMin, SettingsLimits.WindowDepthMax);
            RampSeconds = SettingsLimits.Check(c, "ramp_seconds", RampSeconds, d.RampSeconds, 0, SettingsLimits.SecondsMax);
            GapSeconds = SettingsLimits.Check(c, "gap_seconds", GapSeconds, d.GapSeconds, SettingsLimits.GapSecondsMin, SettingsLimits.GapSecondsMax);
            MinOffsetMm = SettingsLimits.Check(c, "min_offset_mm", MinOffsetMm, d.MinOffsetMm, 0, 10);
            ShrinkAttempts = SettingsLimits.Check(c, "shrink_attempts", ShrinkAttempts, 0, 10);
            FreezeReleaseSeconds = SettingsLimits.Check(c, "freeze_release_seconds", FreezeReleaseSeconds, d.FreezeReleaseSeconds, 0, SettingsLimits.SecondsMax);
            GlideSeconds = SettingsLimits.Check(c, "glide_seconds", GlideSeconds, d.GlideSeconds, 0, SettingsLimits.SecondsMax);

            MapperSettings m = Mapper, dm = d.Mapper;
            m.Gain = SettingsLimits.Check(c, "gain", m.Gain, dm.Gain, SettingsLimits.GainMin, SettingsLimits.GainMax);
            m.DepthGain = SettingsLimits.Check(c, "depth_gain", m.DepthGain, dm.DepthGain, SettingsLimits.DepthGainMin, SettingsLimits.DepthGainMax);
            m.RelativeDistanceMm = SettingsLimits.Check(c, "distance_mm", m.RelativeDistanceMm, dm.RelativeDistanceMm,
                SettingsLimits.ViewingDistanceMinMm, SettingsLimits.ViewingDistanceMaxMm);
            m.ScreenWidthMm = SettingsLimits.Check(c, "screen_width_mm", m.ScreenWidthMm, dm.ScreenWidthMm,
                SettingsLimits.ScreenWidthMinMm, SettingsLimits.ScreenWidthMaxMm);
            m.ScreenHeightMm = SettingsLimits.Check(c, "screen_height_mm", m.ScreenHeightMm, dm.ScreenHeightMm,
                SettingsLimits.ScreenHeightMinMm, SettingsLimits.ScreenHeightMaxMm);
            m.MaxShiftX = SettingsLimits.Check(c, "max_shift_x", m.MaxShiftX, dm.MaxShiftX, SettingsLimits.MaxShiftMin, SettingsLimits.MaxShiftMax);
            m.MaxShiftY = SettingsLimits.Check(c, "max_shift_y", m.MaxShiftY, dm.MaxShiftY, SettingsLimits.MaxShiftMin, SettingsLimits.MaxShiftMax);
            m.MinDepthRatio = SettingsLimits.Check(c, "min_depth_ratio", m.MinDepthRatio, dm.MinDepthRatio,
                SettingsLimits.MinDepthRatioMin, SettingsLimits.MinDepthRatioMax);
            m.MaxDepthRatio = SettingsLimits.Check(c, "max_depth_ratio", m.MaxDepthRatio, dm.MaxDepthRatio,
                SettingsLimits.MaxDepthRatioMin, SettingsLimits.MaxDepthRatioMax);
            m.CentreCaptureSeconds = SettingsLimits.Check(c, "centre_capture_seconds", m.CentreCaptureSeconds, dm.CentreCaptureSeconds, 0, SettingsLimits.SecondsMax);
            m.CentreBlendSeconds = SettingsLimits.Check(c, "centre_blend_seconds", m.CentreBlendSeconds, dm.CentreBlendSeconds, 0, SettingsLimits.SecondsMax);

            SignalSettings s = Signal, ds = d.Signal;
            s.Lateral = SettingsLimits.Check(c, "lateral", s.Lateral, ds.Lateral);
            s.Depth = SettingsLimits.Check(c, "depth", s.Depth, ds.Depth);
            s.PresenceTimeout = SettingsLimits.Check(c, "presence_timeout", s.PresenceTimeout, ds.PresenceTimeout,
                SettingsLimits.PresenceTimeoutMin, SettingsLimits.PresenceTimeoutMax);
            s.FadeOut = SettingsLimits.Check(c, "fade_out", s.FadeOut, ds.FadeOut, 0, SettingsLimits.SecondsMax);
            s.FadeIn = SettingsLimits.Check(c, "fade_in", s.FadeIn, ds.FadeIn, 0, SettingsLimits.SecondsMax);
            s.JumpAfterHoldMm = SettingsLimits.Check(c, "jump_after_hold_mm", s.JumpAfterHoldMm, ds.JumpAfterHoldMm, 1, 1000);
            s.HoldSeconds = SettingsLimits.Check(c, "hold_seconds", s.HoldSeconds, ds.HoldSeconds, 0.05, 10);
            s.MaxSpeed = SettingsLimits.Check(c, "max_speed", s.MaxSpeed, ds.MaxSpeed, 50, 1e5);
            s.MaxDelay = SettingsLimits.Check(c, "max_delay", s.MaxDelay, ds.MaxDelay, 0, 0.5);
            s.MaxExtrapolation = SettingsLimits.Check(c, "max_extrapolation", s.MaxExtrapolation, ds.MaxExtrapolation, 0, 0.1);
            s.MaxFrameSeconds = SettingsLimits.Check(c, "max_frame_seconds", s.MaxFrameSeconds, ds.MaxFrameSeconds, 0.01, 1);
            s.MinPeriod = SettingsLimits.Check(c, "min_period", s.MinPeriod, ds.MinPeriod, 5e-4, 0.05);
            s.MaxPeriod = SettingsLimits.Check(c, "max_period", s.MaxPeriod, ds.MaxPeriod, 0.01, 1);
            if (!(s.MinPeriod < s.MaxPeriod))
            {
                c.Add($"min_period {s.MinPeriod} >= max_period {s.MaxPeriod}: both reset");
                s.MinPeriod = ds.MinPeriod;
                s.MaxPeriod = ds.MaxPeriod;
            }
            s.NominalPeriod = SettingsLimits.Check(c, "nominal_period", s.NominalPeriod, ds.NominalPeriod, s.MinPeriod, s.MaxPeriod);
            return c;
        }
    }

    /// <summary>What to do with the camera this frame.</summary>
    public readonly struct FrameResult
    {
        public FrameResult(bool apply, OffAxisView view, WindowGeometry window, Vec3 eye, Vec3 unclamped, bool clamped,
            double weight, double depth, HeadSignalOutput signal, bool frozen)
        {
            Apply = apply;
            View = view;
            Window = window;
            Eye = eye;
            Unclamped = unclamped;
            Clamped = clamped;
            Weight = weight;
            Depth = depth;
            Signal = signal;
            Frozen = frozen;
        }

        /// <summary>False: leave the game's camera exactly as it is.</summary>
        public bool Apply { get; }

        /// <summary>The camera to write when <see cref="Apply"/>.</summary>
        public OffAxisView View { get; }

        public WindowGeometry Window { get; }

        /// <summary>The virtual eye actually used (mm, screen frame).</summary>
        public Vec3 Eye { get; }

        /// <summary>Where the eye would put the camera before the clamp (world).</summary>
        public Vec3 Unclamped { get; }

        public bool Clamped { get; }

        /// <summary>The effect's weight (gates and ramps).</summary>
        public double Weight { get; }

        /// <summary>The depth the window was placed at, before the depth fraction (m).</summary>
        public double Depth { get; }

        public HeadSignalOutput Signal { get; }

        /// <summary>The eye is held where it was when a freeze began (a tool is being dragged).</summary>
        public bool Frozen { get; }
    }

    /// <summary>
    /// The whole head-coupled camera without Unity: tracker samples in, and
    /// per frame, for the game's camera as it stands, where to put the camera
    /// and which off-axis frustum to give it — or nothing at all.
    /// </summary>
    public sealed class HeadCoupledCamera
    {
        private readonly EffectRamp m_Ramp;
        private readonly ViewParameterGlide m_View = new ViewParameterGlide();
        private bool m_HasFrame;
        private double m_LastFrame;
        private double m_LastDepth = double.NaN;
        private bool m_Frozen;
        private bool m_Releasing;
        private double m_ReleaseStart;
        private double m_ReleaseSeconds;
        private Vec3 m_ReleaseFrom;
        private Vec3 m_LastOffset;
        private bool m_HasSwitches;
        private EyeMode m_LastMode;
        private PoseSource m_LastSource;

        public HeadCoupledCamera(HeadTrackSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Signal = new HeadSignal(settings.Signal);
            Mapper = new EyeMapper(settings.Mapper);
            m_Ramp = new EffectRamp(settings.RampSeconds);
            ViewParameters view = ViewParameters.From(settings);
            Signal.SetViewGain(view.LateralViewGain, view.Gain.Z);
        }

        public HeadTrackSettings Settings { get; }

        public HeadSignal Signal { get; }

        public EyeMapper Mapper { get; }

        /// <summary>
        /// Feeds the tracker's numbers (mm, <see cref="OpenTrackPose.EyeMillimetres"/>:
        /// the display-frame eye for an absolute source, the offset from
        /// OpenTrack's centre for a relative one) that arrived at
        /// <paramref name="time"/> (s); <paramref name="previousArrival"/> is
        /// when the packet before it arrived, a repeat or not (NaN if unknown).
        /// What they mean is applied per frame by the <see cref="Mapper"/>.
        /// </summary>
        public void AddEye(double time, Vec3 eyeMm, double previousArrival = double.NaN)
        {
            Signal.AddSample(time, eyeMm, previousArrival);
        }

        /// <summary>Notes a packet that repeated the previous pose (the source is alive, the head still).</summary>
        public void MarkAlive(double time)
        {
            Signal.MarkAlive(time);
        }

        /// <summary>Feeds a packet's pose that arrived at <paramref name="time"/> (s); see <see cref="AddEye"/>.</summary>
        public void AddPose(double time, OpenTrackPose pose, double previousArrival = double.NaN)
        {
            AddEye(time, pose.EyeMillimetres, previousArrival);
        }

        public void RequestCentre(double now)
        {
            Mapper.RequestCentre(now);
        }

        /// <summary>
        /// The frame at <paramref name="now"/> for the game's
        /// <paramref name="camera"/> aimed at a point <paramref name="depth"/>
        /// metres away. <paramref name="gateOpen"/> false ramps the effect out,
        /// <paramref name="cut"/> drops it at once, and so does a pause in
        /// frames; it ramps in again from there. <paramref name="clamp"/>
        /// may move the wanted position (terrain, water, buildings); the
        /// frustum is built from wherever the camera ends up.
        /// <paramref name="freeze"/> holds the eye where it is (a tool is being
        /// dragged, so the world must stay put under the cursor, or the hold key
        /// is down); when it ends, the eye glides back to the head.
        /// <paramref name="maxGain"/> caps every gain (a construction tool is
        /// active); a new cap glides in like a changed setting.
        /// </summary>
        public FrameResult Compute(double now, CameraFrame camera, double depth, bool gateOpen, bool cut, Func<Vec3, Vec3>? clamp = null,
            bool freeze = false, double maxGain = double.PositiveInfinity)
        {
            if (!IsFinite(now))
            {
                return default;
            }
            bool paused = m_HasFrame && now - m_LastFrame > Settings.GapSeconds;
            m_HasFrame = true;
            m_LastFrame = now;

            Switches(now);
            m_Ramp.Seconds = Settings.RampSeconds;
            HeadSignalOutput signal = Signal.Update(now);
            double weight = cut || paused ? m_Ramp.Cut(now) : m_Ramp.Update(now, gateOpen);
            // While nothing shows, a new setting may apply at once.
            ViewParameters parameters = m_View.Follow(now, ViewParameters.From(Settings).Capped(maxGain), Settings.GlideSeconds, snap: weight <= 0);
            Signal.SetViewGain(parameters.LateralViewGain, parameters.Gain.Z);
            if (IsFinite(depth) && depth > 0)
            {
                m_LastDepth = depth;
            }
            double windowDepth = m_LastDepth;
            if (!IsFinite(windowDepth) || !IsSane(camera))
            {
                return default;
            }
            WindowGeometry window = WindowProjection.Window(camera, parameters.WindowDepthFraction * windowDepth,
                parameters.ScreenWidthMm, parameters.ScreenHeightMm);
            Vec3 neutral = window.NeutralEye;
            bool hadReference = Mapper.HasReference;
            Vec3 mapped = Mapper.Map(now, signal, window.Z0, neutral, parameters);
            if (!hadReference && Mapper.HasReference && Settings.Mapper.Mode == EyeMode.Window)
            {
                // In window mode the first centre moves the limits' origin from
                // the neutral eye to the rest pose: glide across, as after a switch.
                Release(now, Settings.GlideSeconds);
            }
            Vec3 offset = Hold(now, mapped - neutral, freeze) * weight;

            if (!signal.HasEye || weight <= 0 || !IsFinite(offset) || offset.Length < Settings.MinOffsetMm)
            {
                return Skip(window, neutral, camera.Position, weight, windowDepth, signal);
            }
            for (int attempt = 0; attempt <= Settings.ShrinkAttempts; attempt++)
            {
                Vec3 eye = neutral + offset;
                Vec3 wanted = WindowProjection.EyeToWorld(camera, window, eye);
                if (IsFinite(wanted))
                {
                    Vec3 position = clamp != null ? clamp(wanted) : wanted;
                    OffAxisView view = WindowProjection.ViewFrom(camera, window, position);
                    if (view.Valid)
                    {
                        bool clamped = (position - wanted).Length > 0;
                        return new FrameResult(true, view, window, eye, wanted, clamped, weight, windowDepth, signal, m_Frozen);
                    }
                }
                offset *= 0.5;
            }
            return Skip(window, neutral, camera.Position, weight, windowDepth, signal);
        }

        /// <summary>
        /// A switch of mode or source changes what the head means at once; the
        /// view does not: it glides from where it was, as after a freeze. A new
        /// source also drops the old centre. An absolute source without a
        /// centre takes one by itself.
        /// </summary>
        private void Switches(double now)
        {
            EyeMode mode = Settings.Mapper.Mode;
            PoseSource source = Settings.Mapper.Source;
            if (m_HasSwitches && (mode != m_LastMode || source != m_LastSource))
            {
                Release(now, Settings.GlideSeconds);
                if (source != m_LastSource)
                {
                    Mapper.ForgetCentre();
                }
            }
            m_HasSwitches = true;
            m_LastMode = mode;
            m_LastSource = source;
            if (source == PoseSource.Absolute && !Mapper.HasReference && !Mapper.IsCapturing)
            {
                Mapper.RequestCentre(now);
            }
        }

        /// <summary>Glides the view from where it is to the live eye over <paramref name="seconds"/>; a freeze in force keeps it until its own release.</summary>
        private void Release(double now, double seconds)
        {
            if (m_Frozen)
            {
                return;
            }
            m_Releasing = true;
            m_ReleaseStart = now;
            m_ReleaseSeconds = seconds;
            m_ReleaseFrom = m_LastOffset;
        }

        /// <summary>
        /// The eye offset (mm) to use: held while frozen (a tool drag, the hold
        /// key), gliding from there to the live one once the freeze ends — or
        /// once a switch of mode or source has changed the live one.
        /// </summary>
        private Vec3 Hold(double now, Vec3 live, bool freeze)
        {
            if (freeze)
            {
                m_Frozen = true;
                m_Releasing = false;
                return m_LastOffset;
            }
            if (m_Frozen)
            {
                m_Frozen = false;
                Release(now, Settings.FreezeReleaseSeconds);
            }
            Vec3 offset = live;
            if (m_Releasing)
            {
                double progress = m_ReleaseSeconds > 0 ? (now - m_ReleaseStart) / m_ReleaseSeconds : 1;
                if (progress >= 1)
                {
                    m_Releasing = false;
                }
                else
                {
                    offset = m_ReleaseFrom + (live - m_ReleaseFrom) * Smoothstep(progress);
                }
            }
            m_LastOffset = offset;
            return offset;
        }

        private FrameResult Skip(WindowGeometry window, Vec3 neutral, Vec3 position, double weight, double depth, HeadSignalOutput signal) =>
            new FrameResult(false, default, window, neutral, position, false, weight, depth, signal, m_Frozen);

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool IsFinite(Vec3 v) => IsFinite(v.X) && IsFinite(v.Y) && IsFinite(v.Z);

        /// <summary>A camera the window can be built from: finite, with a frustum of positive width and height.</summary>
        private static bool IsSane(CameraFrame camera)
        {
            FrustumTangents t = camera.Tangents;
            return IsFinite(camera.Position) && IsFinite(camera.Right) && IsFinite(camera.Up) && IsFinite(camera.Forward)
                && IsFinite(t.Left) && IsFinite(t.Right) && IsFinite(t.Bottom) && IsFinite(t.Top)
                && t.Right > t.Left && t.Top > t.Bottom && camera.Near > 0;
        }

        private static double Smoothstep(double p)
        {
            p = p < 0 ? 0 : p > 1 ? 1 : p;
            return p * p * (3 - 2 * p);
        }
    }
}
