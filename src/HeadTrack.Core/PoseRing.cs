using System;
using System.Collections.Generic;

namespace HeadTrack.Core
{
    /// <summary>One received pose and when it arrived (Stopwatch ticks).</summary>
    public readonly struct PoseSample
    {
        public PoseSample(long timestamp, OpenTrackPose pose, long previousArrival = 0)
        {
            Timestamp = timestamp;
            Pose = pose;
            PreviousArrival = previousArrival;
        }

        public long Timestamp { get; }

        public OpenTrackPose Pose { get; }

        /// <summary>When the packet before this one arrived, a repeat or not (0 for the first).</summary>
        public long PreviousArrival { get; }
    }

    /// <summary>
    /// The hand-over between the UDP thread and the game's main thread: a
    /// fixed-size ring that keeps every distinct pose with its arrival time.
    /// A pose that repeats the previous one bit for bit (OpenTrack re-sends the
    /// last pose at 250 Hz) is dropped, so the filter sees the tracker's own
    /// rate. When the reader falls behind, the oldest samples are overwritten.
    /// </summary>
    public sealed class PoseRing
    {
        private readonly object m_Lock = new object();
        private readonly PoseSample[] m_Samples;
        private long m_Count;
        private bool m_HasLast;
        private OpenTrackPose m_Last;
        private long m_LastArrival;
        private long m_Read;

        public PoseRing(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            m_Samples = new PoseSample[capacity];
        }

        /// <summary>Poses dropped as bitwise repeats of the previous one.</summary>
        public long Duplicates { get; private set; }

        /// <summary>Samples overwritten before the reader took them.</summary>
        public long Overwritten { get; private set; }

        /// <summary>
        /// When the latest packet arrived, a repeat or not (0 before any): a
        /// still head sends nothing but repeats, and is still there.
        /// </summary>
        public long LastArrival
        {
            get
            {
                lock (m_Lock)
                {
                    return m_LastArrival;
                }
            }
        }

        public void Push(long timestamp, OpenTrackPose pose)
        {
            lock (m_Lock)
            {
                long previous = m_LastArrival;
                m_LastArrival = timestamp;
                if (m_HasLast && pose.BitwiseEquals(m_Last))
                {
                    Duplicates++;
                    return;
                }
                m_HasLast = true;
                m_Last = pose;
                if (m_Count - m_Samples.Length >= m_Read)
                {
                    Overwritten++;
                }
                m_Samples[m_Count % m_Samples.Length] = new PoseSample(timestamp, pose, previous);
                m_Count++;
            }
        }

        /// <summary>
        /// Appends, oldest first, every sample still held whose sequence number
        /// is above <paramref name="after"/> (0 for none read yet), and returns
        /// the sequence number of the newest sample, to pass in next time.
        /// </summary>
        public long TakeNewer(long after, List<PoseSample> output)
        {
            lock (m_Lock)
            {
                long first = Math.Max(after, m_Count - m_Samples.Length);
                for (long sequence = first; sequence < m_Count; sequence++)
                {
                    output.Add(m_Samples[sequence % m_Samples.Length]);
                }
                m_Read = m_Count;
                return m_Count;
            }
        }
    }
}
