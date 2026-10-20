using System;

namespace HeadTrack.Core
{
    /// <summary>Everything the head-coupled camera can be tuned with.</summary>
    public sealed class HeadTrackSettings
    {
        public SignalSettings Signal { get; } = new SignalSettings();

        public MapperSettings Mapper { get; } = new MapperSettings();

        /// <summary>The window's depth as a fraction of the aimed point's depth (k).</summary>
        public double WindowDepthFraction { get; set; } = 1.0;

        /// <summary>Time constant of the window depth's low-pass (s).</summary>
        public double DepthSmoothingSeconds { get; set; } = 0.3;

        /// <summary>How long the effect takes to come in or go out when a gate opens or closes (s).</summary>
        public double RampSeconds { get; set; } = 0.4;

        /// <summary>An eye offset below this (mm) writes nothing, so the game stays bit-exact.</summary>
        public double MinOffsetMm { get; set; } = 1e-3;

        /// <summary>How many times an offset that gives no view is halved before the frame gives up.</summary>
        public int ShrinkAttempts { get; set; } = 4;

        /// <summary>How long the camera takes to glide back to the head when a freeze ends (s).</summary>
        public double FreezeReleaseSeconds { get; set; } = 0.3;
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

        /// <summary>The smoothed depth the window was placed at, before the depth fraction (m).</summary>
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
        private readonly ExponentialSmoother m_Depth;
        private bool m_HasFrame;
        private double m_LastFrame;
        private bool m_Frozen;
        private bool m_Releasing;
        private double m_ReleaseStart;
        private Vec3 m_ReleaseFrom;
        private Vec3 m_LastOffset;

        public HeadCoupledCamera(HeadTrackSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Signal = new HeadSignal(settings.Signal);
            Mapper = new EyeMapper(settings.Mapper);
            m_Ramp = new EffectRamp(settings.RampSeconds);
            m_Depth = new ExponentialSmoother(settings.DepthSmoothingSeconds);
        }

        public HeadTrackSettings Settings { get; }

        public HeadSignal Signal { get; }

        public EyeMapper Mapper { get; }

        /// <summary>
        /// Feeds an eye position (mm) that arrived at <paramref name="time"/>
        /// (s); <paramref name="previousArrival"/> is when the packet before it
        /// arrived, a repeat or not (NaN if unknown).
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
            AddEye(time, Mapper.SourceEye(pose), previousArrival);
        }

        public void RequestCentre(double now)
        {
            Mapper.RequestCentre(now);
        }

        /// <summary>
        /// The frame at <paramref name="now"/> for the game's
        /// <paramref name="camera"/> aimed at a point <paramref name="depth"/>
        /// metres away. <paramref name="gateOpen"/> false ramps the effect out,
        /// <paramref name="cut"/> drops it at once. <paramref name="clamp"/>
        /// may move the wanted position (terrain, water, buildings); the
        /// frustum is built from wherever the camera ends up.
        /// <paramref name="freeze"/> holds the eye where it is (a tool is being
        /// dragged, so the world must stay put under the cursor); when it ends,
        /// the eye glides back to the head.
        /// </summary>
        public FrameResult Compute(double now, CameraFrame camera, double depth, bool gateOpen, bool cut, Func<Vec3, Vec3>? clamp = null,
            bool freeze = false)
        {
            double dt = m_HasFrame ? Math.Max(0, now - m_LastFrame) : 0;
            m_HasFrame = true;
            m_LastFrame = now;

            HeadSignalOutput signal = Signal.Update(now);
            double weight;
            if (cut)
            {
                weight = m_Ramp.Cut();
                m_Depth.Reset(depth);
            }
            else
            {
                weight = m_Ramp.Update(now, gateOpen);
            }
            double smoothedDepth = m_Depth.Update(depth, dt);
            WindowGeometry window = WindowProjection.Window(camera, Settings.WindowDepthFraction * smoothedDepth,
                Settings.Mapper.ScreenWidthMm, Settings.Mapper.ScreenHeightMm);
            Vec3 neutral = window.NeutralEye;
            Vec3 mapped = Mapper.Map(now, signal, window.Z0, neutral);
            Vec3 offset = Hold(now, mapped - neutral, freeze) * weight;

            if (!signal.HasEye || weight <= 0 || offset.Length < Settings.MinOffsetMm)
            {
                return Skip(window, neutral, camera.Position, weight, smoothedDepth, signal);
            }
            for (int attempt = 0; attempt <= Settings.ShrinkAttempts; attempt++)
            {
                Vec3 eye = neutral + offset;
                Vec3 wanted = WindowProjection.EyeToWorld(camera, window, eye);
                Vec3 position = clamp != null ? clamp(wanted) : wanted;
                OffAxisView view = WindowProjection.ViewFrom(camera, window, position);
                if (view.Valid)
                {
                    bool clamped = (position - wanted).Length > 0;
                    return new FrameResult(true, view, window, eye, wanted, clamped, weight, smoothedDepth, signal, m_Frozen);
                }
                offset *= 0.5;
            }
            return Skip(window, neutral, camera.Position, weight, smoothedDepth, signal);
        }

        /// <summary>The eye offset (mm) to use: held while frozen, gliding from there to the live one once the freeze ends.</summary>
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
                m_Releasing = true;
                m_ReleaseStart = now;
                m_ReleaseFrom = m_LastOffset;
            }
            Vec3 offset = live;
            if (m_Releasing)
            {
                double progress = Settings.FreezeReleaseSeconds > 0 ? (now - m_ReleaseStart) / Settings.FreezeReleaseSeconds : 1;
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

        private static double Smoothstep(double p)
        {
            p = p < 0 ? 0 : p > 1 ? 1 : p;
            return p * p * (3 - 2 * p);
        }
    }
}
