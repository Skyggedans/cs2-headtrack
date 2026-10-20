using System;

namespace HeadTrack.Core
{
    /// <summary>The three 1€ parameters: cutoff at rest (Hz), speed coefficient (Hz per unit/s), derivative cutoff (Hz).</summary>
    public readonly struct OneEuroParameters
    {
        public OneEuroParameters(double minCutoff, double beta, double dCutoff)
        {
            MinCutoff = minCutoff;
            Beta = beta;
            DCutoff = dCutoff;
        }

        public double MinCutoff { get; }

        public double Beta { get; }

        public double DCutoff { get; }
    }

    /// <summary>
    /// The 1€ filter on an eye position in mm. x and y share one speed, the
    /// length of their filtered velocity, so both get the same cutoff and a
    /// diagonal move is not bent towards the slower axis; z, the distance to
    /// the screen, has its own parameters.
    /// </summary>
    public sealed class EyeFilter
    {
        private const double MinDt = 1e-4;

        private readonly OneEuroParameters m_Lateral;
        private readonly OneEuroParameters m_Depth;
        private Vec3 m_Value;
        private Vec3 m_Velocity;

        public EyeFilter(OneEuroParameters lateral, OneEuroParameters depth)
        {
            m_Lateral = lateral;
            m_Depth = depth;
        }

        public bool HasValue { get; private set; }

        public Vec3 Value => m_Value;

        /// <summary>Filters one sample taken <paramref name="dt"/> seconds after the previous one.</summary>
        public Vec3 Filter(Vec3 x, double dt)
        {
            if (!HasValue)
            {
                HasValue = true;
                m_Value = x;
                m_Velocity = Vec3.Zero;
                return x;
            }
            dt = Math.Max(dt, MinDt);
            Vec3 raw = (x - m_Value) / dt;
            double aLateral = Alpha(m_Lateral.DCutoff, dt);
            double aDepth = Alpha(m_Depth.DCutoff, dt);
            m_Velocity = new Vec3(
                m_Velocity.X + aLateral * (raw.X - m_Velocity.X),
                m_Velocity.Y + aLateral * (raw.Y - m_Velocity.Y),
                m_Velocity.Z + aDepth * (raw.Z - m_Velocity.Z));
            double lateralSpeed = Math.Sqrt(m_Velocity.X * m_Velocity.X + m_Velocity.Y * m_Velocity.Y);
            double a = Alpha(m_Lateral.MinCutoff + m_Lateral.Beta * lateralSpeed, dt);
            double az = Alpha(m_Depth.MinCutoff + m_Depth.Beta * Math.Abs(m_Velocity.Z), dt);
            m_Value = new Vec3(
                m_Value.X + a * (x.X - m_Value.X),
                m_Value.Y + a * (x.Y - m_Value.Y),
                m_Value.Z + az * (x.Z - m_Value.Z));
            return m_Value;
        }

        public void Reset()
        {
            HasValue = false;
            m_Value = Vec3.Zero;
            m_Velocity = Vec3.Zero;
        }

        private static double Alpha(double cutoff, double dt)
        {
            double tau = 1.0 / (2.0 * Math.PI * cutoff);
            return 1.0 / (1.0 + tau / dt);
        }
    }
}
