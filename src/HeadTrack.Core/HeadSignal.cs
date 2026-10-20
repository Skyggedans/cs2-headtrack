using System;
using System.Collections.Generic;

namespace HeadTrack.Core
{
    /// <summary>Tuning of <see cref="HeadSignal"/>; the defaults suit a 33 Hz head tracker and positions in mm.</summary>
    public sealed class SignalSettings
    {
        /// <summary>1€ parameters for x and y (sharing one speed).</summary>
        public OneEuroParameters Lateral { get; set; } = new OneEuroParameters(0.5, 0.02, 2.0);

        /// <summary>1€ parameters for z, the distance to the screen.</summary>
        public OneEuroParameters Depth { get; set; } = new OneEuroParameters(0.3, 0.01, 1.5);

        /// <summary>Seconds without a sample before the face counts as lost.</summary>
        public double PresenceTimeout { get; set; } = 0.5;

        /// <summary>Seconds the presence takes to fall from 1 to 0 once the face is lost.</summary>
        public double FadeOut { get; set; } = 0.6;

        /// <summary>Seconds the presence takes to rise from 0 to 1 once samples return.</summary>
        public double FadeIn { get; set; } = 0.4;

        /// <summary>A pause longer than this many periods restarts the filter on the next sample.</summary>
        public double RestartGapPeriods { get; set; } = 3.0;

        /// <summary>Seconds over which the output glides from where it was to the restarted signal.</summary>
        public double RestartCrossfade { get; set; } = 0.25;

        /// <summary>The output never moves faster than this (mm/s).</summary>
        public double MaxSpeed { get; set; } = 400.0;

        /// <summary>Upper bound of the interpolation delay (s).</summary>
        public double MaxDelay { get; set; } = 0.050;

        /// <summary>How far past the newest sample the output may extrapolate (s).</summary>
        public double MaxExtrapolation { get; set; } = 0.010;

        /// <summary>The period assumed until intervals have been measured (s).</summary>
        public double NominalPeriod { get; set; } = 1.0 / 33.0;
    }

    /// <summary>What <see cref="HeadSignal.Update"/> hands to the frame.</summary>
    public readonly struct HeadSignalOutput
    {
        public HeadSignalOutput(bool hasEye, Vec3 eye, double presence, double age, double period, double delay, bool held)
        {
            HasEye = hasEye;
            Eye = eye;
            Presence = presence;
            Age = age;
            Period = period;
            Delay = delay;
            Held = held;
        }

        /// <summary>False until the first sample.</summary>
        public bool HasEye { get; }

        /// <summary>The eye (mm), filtered and resampled to the frame time.</summary>
        public Vec3 Eye { get; }

        /// <summary>0 when the face is lost, 1 when it is tracked, smooth in between.</summary>
        public double Presence { get; }

        /// <summary>Seconds since the newest sample arrived.</summary>
        public double Age { get; }

        /// <summary>The estimated sample period (s).</summary>
        public double Period { get; }

        /// <summary>How far behind the frame time the eye is sampled (s).</summary>
        public double Delay { get; }

        /// <summary>The frame ran past the newest sample (plus the extrapolation allowance) and held.</summary>
        public bool Held { get; }
    }

    /// <summary>
    /// Turns the tracker's samples (about 33 Hz, arrival-stamped) into one eye
    /// position per game frame:
    /// each sample goes through the 1€ <see cref="EyeFilter"/> when it arrives;
    /// each frame reads the filtered track at now − δ, δ = T̂ + 2σ (the measured
    /// period and its jitter), interpolating between the samples around that
    /// time, so the eye moves smoothly at any frame rate. A pause longer than a
    /// few periods restarts the filter and glides the output to the new track;
    /// the output speed is limited, so no gap or re-acquisition can snap the
    /// camera; and the presence falls to 0 after a timeout of silence and rises
    /// again when samples return.
    /// </summary>
    public sealed class HeadSignal
    {
        private const int HistoryLength = 16;
        private const int IntervalCount = 32;

        private readonly SignalSettings m_Settings;
        private readonly EyeFilter m_Filter;
        private readonly List<(double Time, Vec3 Eye)> m_History = new List<(double, Vec3)>(HistoryLength + 1);
        private readonly double[] m_Intervals = new double[IntervalCount];
        private readonly double[] m_Sorted = new double[IntervalCount];
        private int m_IntervalsStored;
        private int m_IntervalsNext;
        private bool m_HasSample;
        private double m_LastSample;
        private bool m_HasUpdate;
        private double m_LastUpdate;
        private double m_PresenceProgress;
        private bool m_HasOutput;
        private Vec3 m_Output;
        private bool m_RestartPending;
        private bool m_Crossfading;
        private Vec3 m_CrossfadeFrom;
        private double m_CrossfadeStart;

        public HeadSignal(SignalSettings settings)
        {
            m_Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            m_Filter = new EyeFilter(settings.Lateral, settings.Depth);
        }

        /// <summary>Feeds one sample; samples must come in arrival order.</summary>
        public void AddSample(double time, Vec3 eye)
        {
            double dt = m_Settings.NominalPeriod;
            if (m_HasSample)
            {
                double interval = time - m_LastSample;
                if (interval > m_Settings.RestartGapPeriods * EstimatePeriod())
                {
                    m_Filter.Reset();
                    m_History.Clear();
                    m_RestartPending = true;
                }
                else if (interval > 0)
                {
                    RecordInterval(interval);
                    dt = interval;
                }
            }
            Vec3 filtered = m_Filter.Filter(eye, dt);
            m_History.Add((time, filtered));
            if (m_History.Count > HistoryLength)
            {
                m_History.RemoveAt(0);
            }
            m_HasSample = true;
            m_LastSample = time;
        }

        /// <summary>The eye for a frame at <paramref name="now"/> (seconds, same clock as the samples).</summary>
        public HeadSignalOutput Update(double now)
        {
            double frameDt = m_HasUpdate ? Math.Max(0, now - m_LastUpdate) : 0;
            m_HasUpdate = true;
            m_LastUpdate = now;
            if (!m_HasSample)
            {
                return new HeadSignalOutput(false, Vec3.Zero, 0, double.PositiveInfinity, EstimatePeriod(), 0, true);
            }

            double age = now - m_LastSample;
            bool present = age <= m_Settings.PresenceTimeout;
            m_PresenceProgress = present
                ? Math.Min(1, m_PresenceProgress + frameDt / m_Settings.FadeIn)
                : Math.Max(0, m_PresenceProgress - frameDt / m_Settings.FadeOut);

            double period = EstimatePeriod();
            double delay = Clamp(period + 2 * EstimateJitter(period), 1.1 * period, m_Settings.MaxDelay);
            Vec3 value = SampleHistory(now - delay, out bool held);

            if (m_RestartPending)
            {
                m_RestartPending = false;
                if (m_HasOutput)
                {
                    m_Crossfading = true;
                    m_CrossfadeFrom = m_Output;
                    m_CrossfadeStart = now;
                }
            }
            if (m_Crossfading)
            {
                double progress = (now - m_CrossfadeStart) / m_Settings.RestartCrossfade;
                if (progress >= 1)
                {
                    m_Crossfading = false;
                }
                else
                {
                    value = m_CrossfadeFrom + (value - m_CrossfadeFrom) * Smoothstep(progress);
                }
            }

            if (m_HasOutput)
            {
                Vec3 step = value - m_Output;
                double limit = m_Settings.MaxSpeed * frameDt;
                double length = step.Length;
                if (length > limit)
                {
                    value = length > 0 ? m_Output + step * (limit / length) : m_Output;
                }
            }
            m_Output = value;
            m_HasOutput = true;
            return new HeadSignalOutput(true, value, Smoothstep(m_PresenceProgress), age, period, delay, held);
        }

        public void Reset()
        {
            m_Filter.Reset();
            m_History.Clear();
            m_IntervalsStored = 0;
            m_IntervalsNext = 0;
            m_HasSample = false;
            m_HasUpdate = false;
            m_PresenceProgress = 0;
            m_HasOutput = false;
            m_RestartPending = false;
            m_Crossfading = false;
        }

        private void RecordInterval(double interval)
        {
            m_Intervals[m_IntervalsNext] = interval;
            m_IntervalsNext = (m_IntervalsNext + 1) % IntervalCount;
            m_IntervalsStored = Math.Min(m_IntervalsStored + 1, IntervalCount);
        }

        /// <summary>The median of the recent intervals, robust to the odd late packet.</summary>
        private double EstimatePeriod()
        {
            if (m_IntervalsStored == 0)
            {
                return m_Settings.NominalPeriod;
            }
            Array.Copy(m_Intervals, m_Sorted, m_IntervalsStored);
            Array.Sort(m_Sorted, 0, m_IntervalsStored);
            int middle = m_IntervalsStored / 2;
            return m_IntervalsStored % 2 == 1
                ? m_Sorted[middle]
                : 0.5 * (m_Sorted[middle - 1] + m_Sorted[middle]);
        }

        /// <summary>The RMS deviation of the recent intervals from the period.</summary>
        private double EstimateJitter(double period)
        {
            if (m_IntervalsStored == 0)
            {
                return 0;
            }
            double sum = 0;
            for (int i = 0; i < m_IntervalsStored; i++)
            {
                double d = m_Intervals[i] - period;
                sum += d * d;
            }
            return Math.Sqrt(sum / m_IntervalsStored);
        }

        private Vec3 SampleHistory(double time, out bool held)
        {
            held = false;
            int count = m_History.Count;
            if (count == 0)
            {
                held = true;
                return m_Output;
            }
            if (time <= m_History[0].Time)
            {
                return m_History[0].Eye;
            }
            for (int i = count - 1; i >= 1; i--)
            {
                (double t0, Vec3 e0) = m_History[i - 1];
                (double t1, Vec3 e1) = m_History[i];
                if (time >= t0 && time <= t1)
                {
                    double span = t1 - t0;
                    return span > 0 ? e0 + (e1 - e0) * ((time - t0) / span) : e1;
                }
            }
            (double tLast, Vec3 eLast) = m_History[count - 1];
            double over = time - tLast;
            if (count < 2)
            {
                held = true;
                return eLast;
            }
            (double tPrev, Vec3 ePrev) = m_History[count - 2];
            double segment = tLast - tPrev;
            Vec3 slope = segment > 0 ? (eLast - ePrev) / segment : Vec3.Zero;
            if (over > m_Settings.MaxExtrapolation)
            {
                held = true;
                over = m_Settings.MaxExtrapolation;
            }
            return eLast + slope * over;
        }

        private static double Smoothstep(double p)
        {
            p = Clamp(p, 0, 1);
            return p * p * (3 - 2 * p);
        }

        private static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;
    }
}
