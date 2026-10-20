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

        /// <summary>
        /// Seconds without any packet before the face counts as lost; a
        /// sample after a longer silence starts the filter afresh.
        /// </summary>
        public double PresenceTimeout { get; set; } = 0.5;

        /// <summary>Seconds the presence takes to fall from 1 to 0 once the face is lost.</summary>
        public double FadeOut { get; set; } = 0.6;

        /// <summary>Seconds the presence takes to rise from 0 to 1 once samples return.</summary>
        public double FadeIn { get; set; } = 0.4;

        /// <summary>
        /// A sample this far (mm) from a pose the source held for
        /// <see cref="HoldSeconds"/> is a re-acquisition, not a movement:
        /// OpenTrack keeps sending the last pose while the face is lost.
        /// </summary>
        public double JumpAfterHoldMm { get; set; } = 50.0;

        /// <summary>How long (s) a pose must have been held for a jump from it to count as a re-acquisition.</summary>
        public double HoldSeconds { get; set; } = 0.3;

        /// <summary>The output never moves faster than this (mm/s); deliberate peeks stay below it.</summary>
        public double MaxSpeed { get; set; } = 1500.0;

        /// <summary>Upper bound of the interpolation delay (s), unless the period itself is longer.</summary>
        public double MaxDelay { get; set; } = 0.050;

        /// <summary>How far past the newest sample the output may extrapolate (s).</summary>
        public double MaxExtrapolation { get; set; } = 0.010;

        /// <summary>The period assumed until intervals have been measured (s).</summary>
        public double NominalPeriod { get; set; } = 1.0 / 33.0;

        /// <summary>
        /// Intervals outside [MinPeriod, MaxPeriod] (s) are not the sample
        /// period: bursts after a receive stall, or the pauses of a still head.
        /// </summary>
        public double MinPeriod { get; set; } = 0.002;

        /// <inheritdoc cref="MinPeriod"/>
        public double MaxPeriod { get; set; } = 0.100;

        /// <summary>A frame longer than this (s) counts as this long, so fades and the speed limit never finish in one frame after a pause.</summary>
        public double MaxFrameSeconds { get; set; } = 0.1;
    }

    /// <summary>What <see cref="HeadSignal.Update"/> hands to the frame.</summary>
    public readonly struct HeadSignalOutput
    {
        public HeadSignalOutput(bool hasEye, Vec3 eye, double presence, double age, double period, double delay, bool held,
            bool settling)
        {
            HasEye = hasEye;
            Eye = eye;
            Presence = presence;
            Age = age;
            Period = period;
            Delay = delay;
            Held = held;
            Settling = settling;
        }

        /// <summary>False until the first sample.</summary>
        public bool HasEye { get; }

        /// <summary>The eye (mm), filtered and resampled to the frame time.</summary>
        public Vec3 Eye { get; }

        /// <summary>0 when the face is lost, 1 when it is tracked, smooth in between.</summary>
        public double Presence { get; }

        /// <summary>Seconds since the newest packet arrived.</summary>
        public double Age { get; }

        /// <summary>The estimated sample period (s).</summary>
        public double Period { get; }

        /// <summary>How far behind the frame time the eye is sampled (s).</summary>
        public double Delay { get; }

        /// <summary>The frame ran past the newest sample (plus the extrapolation allowance) and held.</summary>
        public bool Held { get; }

        /// <summary>The eye does not follow the head this frame: it is fading out a re-acquisition, or held at the speed limit.</summary>
        public bool Settling { get; }
    }

    /// <summary>
    /// Turns the tracker's samples (arrival-stamped, 33 Hz from the tracker,
    /// up to 250 Hz through OpenTrack's filter) into one eye position per game
    /// frame. Each sample goes through the 1€ <see cref="EyeFilter"/> when it
    /// arrives; each frame reads the filtered track at now − δ, δ = T̂ + 2σ
    /// (the measured period and its jitter), interpolating between the
    /// samples around that time, so the eye moves smoothly at any frame rate.
    /// The presence falls to 0 after a timeout without packets and rises when
    /// they return. A re-acquisition — a sample after a loss, or a jump from a
    /// pose the source held — never shows as a camera movement: the view fades
    /// out from where it was and back in on the new track.
    /// </summary>
    public sealed class HeadSignal
    {
        private const int HistoryLength = 16;
        private const int IntervalCount = 32;

        /// <summary>How fast δ may change (s per s), so the sampling time never runs backwards.</summary>
        private const double DelaySlew = 0.05;

        private readonly SignalSettings m_Settings;
        private readonly EyeFilter m_Filter;
        private readonly List<(double Time, Vec3 Eye)> m_History = new List<(double, Vec3)>(HistoryLength + 1);
        private readonly double[] m_Intervals = new double[IntervalCount];
        private readonly double[] m_Sorted = new double[IntervalCount];
        private int m_IntervalsStored;
        private int m_IntervalsNext;
        private bool m_HasSample;
        private double m_LastSample;
        private Vec3 m_LastRaw;
        private double m_LastFilterTime;
        private double m_LastAlive;
        private bool m_HasUpdate;
        private double m_LastUpdate;
        private double m_PresenceProgress;
        private bool m_HasOutput;
        private Vec3 m_Output;
        private bool m_HasDelay;
        private double m_Delay;
        private bool m_RestartPending;
        private bool m_Dipping;
        private Vec3 m_DipHold;
        private double m_LateralViewGain = 1;
        private double m_DepthViewGain = 1;

        public HeadSignal(SignalSettings settings)
        {
            m_Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            m_Filter = new EyeFilter(settings.Lateral, settings.Depth);
        }

        /// <summary>Re-acquisitions so far: a sample after a loss, or a jump from a held pose.</summary>
        public long Restarts { get; private set; }

        /// <summary>
        /// How much the view amplifies the head sideways and in depth (the
        /// magnitudes of the gains). The filter measures speed in what the
        /// user sees, so its speed coefficient β is scaled by these: the view
        /// at gain g then follows a head track exactly as it follows g times
        /// that track at gain 1, and a peek keeps its latency at any gain.
        /// The cutoff at rest and the derivative cutoff stay as set.
        /// </summary>
        public void SetViewGain(double lateral, double depth)
        {
            m_LateralViewGain = IsFinite(lateral) ? Math.Abs(lateral) : 1;
            m_DepthViewGain = IsFinite(depth) ? Math.Abs(depth) : 1;
        }

        /// <summary>
        /// Notes that the source sent a packet at <paramref name="time"/> that
        /// repeated the previous pose: no new sample, but the head is there,
        /// so the presence holds — and the pose holds, so once a period the
        /// filter takes it again, as the tracker would report it. A head that
        /// stops then settles where it stopped, not where the filter lagged.
        /// </summary>
        public void MarkAlive(double time)
        {
            if (!m_HasSample || !(time > m_LastAlive))
            {
                return;
            }
            m_LastAlive = time;
            double sinceFilter = time - m_LastFilterTime;
            if (sinceFilter >= EstimatePeriod())
            {
                AddToHistory(time, Filter(m_LastRaw, sinceFilter));
                m_LastFilterTime = time;
            }
        }

        /// <summary>
        /// Feeds one sample that arrived at <paramref name="time"/>;
        /// <paramref name="previousArrival"/> is when the packet before it
        /// arrived, a repeat or not (NaN if unknown). Samples must come in
        /// arrival order.
        /// </summary>
        public void AddSample(double time, Vec3 eye, double previousArrival = double.NaN)
        {
            double dt = m_Settings.NominalPeriod;
            if (m_HasSample)
            {
                double heard = Math.Max(m_LastSample, m_LastAlive);
                if (previousArrival > heard)
                {
                    heard = previousArrival;
                }
                double interval = time - m_LastSample;
                bool lost = time - heard > m_Settings.PresenceTimeout;
                bool jumped = interval >= m_Settings.HoldSeconds && (eye - m_LastRaw).Length > m_Settings.JumpAfterHoldMm;
                if (lost || jumped)
                {
                    m_Filter.Reset();
                    m_History.Clear();
                    m_RestartPending = true;
                    Restarts++;
                }
                else
                {
                    if (interval > 0)
                    {
                        RecordInterval(interval);
                    }
                    dt = Math.Max(m_Settings.MinPeriod, time - m_LastFilterTime);
                }
            }
            AddToHistory(time, Filter(eye, dt));
            m_HasSample = true;
            m_LastSample = time;
            m_LastFilterTime = time;
            m_LastRaw = eye;
            if (time > m_LastAlive)
            {
                m_LastAlive = time;
            }
        }

        /// <summary>The eye for a frame at <paramref name="now"/> (seconds, same clock as the samples).</summary>
        public HeadSignalOutput Update(double now)
        {
            double frameDt = m_HasUpdate ? Clamp(now - m_LastUpdate, 0, m_Settings.MaxFrameSeconds) : 0;
            m_HasUpdate = true;
            m_LastUpdate = now;
            if (!m_HasSample)
            {
                return new HeadSignalOutput(false, Vec3.Zero, 0, double.PositiveInfinity, EstimatePeriod(), 0, true, false);
            }

            double age = now - Math.Max(m_LastSample, m_LastAlive);
            if (m_RestartPending)
            {
                m_RestartPending = false;
                if (m_HasOutput && m_PresenceProgress > 0)
                {
                    // Something shows: fade it out where it is first.
                    m_Dipping = true;
                    m_DipHold = m_Output;
                }
                else
                {
                    // Nothing shows: take the new track as it is.
                    m_HasOutput = false;
                }
            }

            double period = EstimatePeriod();
            double target = Clamp(period + 2 * EstimateJitter(period), 1.1 * period, Math.Max(m_Settings.MaxDelay, 1.1 * period));
            if (!m_HasDelay)
            {
                m_Delay = target;
                m_HasDelay = true;
            }
            else
            {
                double slew = DelaySlew * frameDt;
                m_Delay += Clamp(target - m_Delay, -slew, slew);
            }
            Vec3 value = SampleHistory(now - m_Delay, out bool held);

            if (m_Dipping)
            {
                m_PresenceProgress = Math.Max(0, m_PresenceProgress - Rate(frameDt, m_Settings.FadeOut));
                if (m_PresenceProgress > 0)
                {
                    return new HeadSignalOutput(true, m_DipHold, Smoothstep(m_PresenceProgress), age, period, m_Delay, held, true);
                }
                // Faded out: the new track takes over at zero presence.
                m_Dipping = false;
                m_HasOutput = false;
            }
            else
            {
                bool present = age <= m_Settings.PresenceTimeout;
                m_PresenceProgress = present
                    ? Math.Min(1, m_PresenceProgress + Rate(frameDt, m_Settings.FadeIn))
                    : Math.Max(0, m_PresenceProgress - Rate(frameDt, m_Settings.FadeOut));
            }

            bool settling = false;
            if (m_HasOutput)
            {
                Vec3 step = value - m_Output;
                double limit = m_Settings.MaxSpeed * frameDt;
                double length = step.Length;
                if (length > limit)
                {
                    value = length > 0 ? m_Output + step * (limit / length) : m_Output;
                    settling = true;
                }
            }
            m_Output = value;
            m_HasOutput = true;
            return new HeadSignalOutput(true, value, Smoothstep(m_PresenceProgress), age, period, m_Delay, held, settling);
        }

        public void Reset()
        {
            m_Filter.Reset();
            m_History.Clear();
            m_IntervalsStored = 0;
            m_IntervalsNext = 0;
            m_HasSample = false;
            m_LastAlive = 0;
            m_HasUpdate = false;
            m_PresenceProgress = 0;
            m_HasOutput = false;
            m_HasDelay = false;
            m_RestartPending = false;
            m_Dipping = false;
        }

        /// <summary>One sample through the 1€ filter with the settings as they are now.</summary>
        private Vec3 Filter(Vec3 eye, double dt)
        {
            m_Filter.Lateral = Scaled(m_Settings.Lateral, m_LateralViewGain);
            m_Filter.Depth = Scaled(m_Settings.Depth, m_DepthViewGain);
            return m_Filter.Filter(eye, dt);
        }

        private static OneEuroParameters Scaled(OneEuroParameters parameters, double viewGain) =>
            new OneEuroParameters(parameters.MinCutoff, parameters.Beta * viewGain, parameters.DCutoff);

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private void AddToHistory(double time, Vec3 filtered)
        {
            m_History.Add((time, filtered));
            if (m_History.Count > HistoryLength)
            {
                m_History.RemoveAt(0);
            }
        }

        /// <summary>Keeps only intervals that can be the sample period; bursts and pauses would drag the estimate away for good.</summary>
        private void RecordInterval(double interval)
        {
            if (interval < m_Settings.MinPeriod || interval > m_Settings.MaxPeriod)
            {
                return;
            }
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

        /// <summary>
        /// The RMS deviation from the period of the intervals up to 1.5
        /// periods: a dropped sample leaves a gap no delay below two periods
        /// could bridge, so it must not inflate the delay.
        /// </summary>
        private double EstimateJitter(double period)
        {
            double sum = 0;
            int count = 0;
            for (int i = 0; i < m_IntervalsStored; i++)
            {
                double interval = m_Intervals[i];
                if (interval <= 1.5 * period)
                {
                    double d = interval - period;
                    sum += d * d;
                    count++;
                }
            }
            return count > 0 ? Math.Sqrt(sum / count) : 0;
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

        private static double Rate(double dt, double seconds) => seconds > 0 ? dt / seconds : 1;

        private static double Smoothstep(double p)
        {
            p = Clamp(p, 0, 1);
            return p * p * (3 - 2 * p);
        }

        private static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;
    }
}
