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
    }

    /// <summary>What to do with the camera this frame.</summary>
    public readonly struct FrameResult
    {
        public FrameResult(bool apply, OffAxisView view, WindowGeometry window, Vec3 eye, Vec3 unclamped, bool clamped,
            double weight, double depth, HeadSignalOutput signal)
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

        /// <summary>Feeds an eye position (mm) that arrived at <paramref name="time"/> (s).</summary>
        public void AddEye(double time, Vec3 eyeMm)
        {
            Signal.AddSample(time, eyeMm);
        }

        /// <summary>Feeds a packet's pose that arrived at <paramref name="time"/> (s).</summary>
        public void AddPose(double time, OpenTrackPose pose)
        {
            AddEye(time, Mapper.SourceEye(pose));
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
        /// </summary>
        public FrameResult Compute(double now, CameraFrame camera, double depth, bool gateOpen, bool cut, Func<Vec3, Vec3>? clamp = null)
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
            Vec3 offset = (mapped - neutral) * weight;

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
                    return new FrameResult(true, view, window, eye, wanted, clamped, weight, smoothedDepth, signal);
                }
                offset *= 0.5;
            }
            return Skip(window, neutral, camera.Position, weight, smoothedDepth, signal);
        }

        private static FrameResult Skip(WindowGeometry window, Vec3 neutral, Vec3 position, double weight, double depth, HeadSignalOutput signal) =>
            new FrameResult(false, default, window, neutral, position, false, weight, depth, signal);
    }
}
