using System;

namespace HeadTrack.Core
{
    /// <summary>
    /// The 1€ filter (Casiez, Roussel, Vogel, CHI 2012): a first-order low-pass
    /// whose cutoff rises with the signal's speed, so a still head is smoothed
    /// hard while a deliberate move keeps little lag.
    /// cutoff = minCutoff + beta·|filtered derivative|; with positions in mm,
    /// beta is in Hz per mm/s.
    /// </summary>
    public sealed class OneEuroFilter
    {
        /// <summary>Time steps below this are treated as this, so a burst of packets cannot divide by zero.</summary>
        private const double MinDt = 1e-4;

        private readonly double m_MinCutoff;
        private readonly double m_Beta;
        private readonly double m_DCutoff;
        private double m_X;
        private double m_DX;

        public OneEuroFilter(double minCutoff, double beta, double dCutoff)
        {
            m_MinCutoff = minCutoff;
            m_Beta = beta;
            m_DCutoff = dCutoff;
        }

        public bool HasValue { get; private set; }

        public double Value => m_X;

        /// <summary>The filtered derivative (units per second).</summary>
        public double Derivative => m_DX;

        /// <summary>Filters one sample taken <paramref name="dt"/> seconds after the previous one.</summary>
        public double Filter(double x, double dt)
        {
            if (!HasValue)
            {
                HasValue = true;
                m_X = x;
                m_DX = 0;
                return x;
            }
            dt = Math.Max(dt, MinDt);
            double raw = (x - m_X) / dt;
            m_DX += Alpha(m_DCutoff, dt) * (raw - m_DX);
            double cutoff = m_MinCutoff + m_Beta * Math.Abs(m_DX);
            m_X += Alpha(cutoff, dt) * (x - m_X);
            return m_X;
        }

        public void Reset()
        {
            HasValue = false;
            m_X = 0;
            m_DX = 0;
        }

        private static double Alpha(double cutoff, double dt)
        {
            double tau = 1.0 / (2.0 * Math.PI * cutoff);
            return 1.0 / (1.0 + tau / dt);
        }
    }
}
