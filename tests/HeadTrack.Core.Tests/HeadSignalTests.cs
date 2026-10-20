using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class HeadSignalTests
{
    private const double Period = 1.0 / 33.0;

    /// <summary>A tracker at 33 Hz with normal arrival jitter, and a game at a jittery frame rate.</summary>
    private sealed class Rig
    {
        private readonly Random m_Rng;
        private readonly double m_JitterSigma;
        private readonly Queue<(double Arrival, Vec3 Eye)> m_InFlight = new();
        private double m_NextSample;

        public Rig(int seed, double jitterSigma = 0.002)
        {
            m_Rng = new Random(seed);
            m_JitterSigma = jitterSigma;
        }

        public HeadSignal Signal { get; } = new(new SignalSettings());

        public List<(double Time, HeadSignalOutput Output)> Frames { get; } = new();

        /// <summary>Runs from <paramref name="from"/> to <paramref name="to"/> seconds; <paramref name="eye"/> gives the true eye, null while the face is lost.</summary>
        public void Run(double from, double to, double fps, Func<double, Vec3?> eye)
        {
            if (m_NextSample < from)
            {
                m_NextSample = from;
            }
            double now = from;
            while (now < to)
            {
                double frame = (1.0 / fps) * (1.0 + 0.15 * (2 * m_Rng.NextDouble() - 1));
                now += frame;
                // Samples are taken on the tracker's clock and arrive a little
                // later; one that arrives after this frame is delivered in a
                // later frame, as a socket would.
                while (m_NextSample <= now)
                {
                    Vec3? value = eye(m_NextSample);
                    if (value.HasValue)
                    {
                        m_InFlight.Enqueue((m_NextSample + Math.Abs(Gaussian()) * m_JitterSigma, value.Value));
                    }
                    m_NextSample += Period;
                }
                while (m_InFlight.Count > 0 && m_InFlight.Peek().Arrival <= now)
                {
                    (double arrival, Vec3 value) = m_InFlight.Dequeue();
                    Signal.AddSample(arrival, value);
                }
                Frames.Add((now, Signal.Update(now)));
            }
        }

        private double Gaussian()
        {
            double u1 = 1.0 - m_Rng.NextDouble();
            double u2 = m_Rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }

    /// <summary>
    /// Packets a source sends every <paramref name="packetPeriod"/> carrying
    /// the tracker's latest sample (taken every <paramref name="samplePeriod"/>);
    /// one that repeats the previous pose bit for bit is marked, as the ring
    /// drops it. <paramref name="eye"/> null: the source sends nothing.
    /// </summary>
    private static List<(double Arrival, Vec3 Eye, bool Repeat)> Packets(double from, double to, double packetPeriod,
        double samplePeriod, Func<double, Vec3?> eye)
    {
        var packets = new List<(double, Vec3, bool)>();
        bool hasPrevious = false;
        Vec3 previous = default;
        int count = (int)Math.Round((to - from) / packetPeriod);
        for (int i = 0; i < count; i++)
        {
            double t = from + i * packetPeriod;
            Vec3? value = eye(Math.Floor(t / samplePeriod + 1e-9) * samplePeriod);
            if (!value.HasValue)
            {
                continue;
            }
            Vec3 v = value.Value;
            bool repeat = hasPrevious && v.X == previous.X && v.Y == previous.Y && v.Z == previous.Z;
            packets.Add((t, v, repeat));
            previous = v;
            hasPrevious = true;
        }
        return packets;
    }

    /// <summary>
    /// Plays packets the way the mod does: each frame, every distinct packet
    /// that has arrived goes to AddSample with the arrival of the packet before
    /// it, then MarkAlive with the latest arrival. While
    /// <paramref name="framesPaused"/> holds there are no frames at all.
    /// </summary>
    private static List<(double Time, HeadSignalOutput Output)> Drive(HeadSignal signal,
        List<(double Arrival, Vec3 Eye, bool Repeat)> packets, double from, double to, double fps,
        Func<double, bool>? framesPaused = null)
    {
        var frames = new List<(double, HeadSignalOutput)>();
        int next = 0;
        double previous = double.NaN;
        int count = (int)Math.Round((to - from) * fps);
        for (int i = 0; i < count; i++)
        {
            double now = from + i / fps;
            if (framesPaused?.Invoke(now) == true)
            {
                continue;
            }
            while (next < packets.Count && packets[next].Arrival <= now)
            {
                (double arrival, Vec3 eye, bool repeat) = packets[next++];
                if (!repeat)
                {
                    signal.AddSample(arrival, eye, previous);
                }
                previous = arrival;
            }
            if (!double.IsNaN(previous))
            {
                signal.MarkAlive(previous);
            }
            frames.Add((now, signal.Update(now)));
        }
        return frames;
    }

    private static Vec3 Sway(double t) => new(60 * Math.Sin(2 * Math.PI * 0.4 * t), 0, 600);

    private static double Std(IEnumerable<double> values)
    {
        double[] v = values.ToArray();
        double mean = v.Average();
        return Math.Sqrt(v.Select(x => (x - mean) * (x - mean)).Average());
    }

    [Theory]
    [InlineData(60.0)]
    [InlineData(144.0)]
    public void A_steady_move_comes_out_smooth_at_any_frame_rate(double fps)
    {
        var rig = new Rig(seed: 7);
        rig.Run(0, 4, fps, t => new Vec3(100.0 * t, 0, 600));

        var steady = rig.Frames.Where(f => f.Time > 1.0).ToList();
        for (int i = 1; i < steady.Count; i++)
        {
            Assert.True(steady[i].Output.Eye.X >= steady[i - 1].Output.Eye.X - 1e-9, $"backwards at {steady[i].Time:F3} s");
        }
        double lagStd = Std(steady.Select(f => 100.0 * f.Time - f.Output.Eye.X));
        Assert.True(lagStd < 0.6, $"lag jitter {lagStd:F3} mm at {fps} fps");
    }

    [Fact]
    public void With_ordinary_arrival_jitter_frames_almost_never_have_to_hold()
    {
        var rig = new Rig(seed: 11);
        rig.Run(0, 60, 60.0, t => new Vec3(30 * Math.Sin(t), 0, 600));

        var steady = rig.Frames.Where(f => f.Time > 1.0).ToList();
        double heldShare = steady.Count(f => f.Output.Held) / (double)steady.Count;
        Assert.True(heldShare <= 0.001, $"held {heldShare:P2} of frames");
    }

    [Fact]
    public void The_period_estimate_follows_the_tracker()
    {
        var rig = new Rig(seed: 3);
        rig.Run(0, 3, 60.0, t => new Vec3(0, 0, 600));

        Assert.Equal(Period, rig.Frames[^1].Output.Period, 3);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.3)]
    [InlineData(0.7)]
    [InlineData(1.5)]
    public void A_gap_in_the_stream_never_moves_the_eye_faster_than_the_slew_limit(double gap)
    {
        var rig = new Rig(seed: 5);
        double lostAt = 2.0;
        Func<double, Vec3?> eye = t =>
            t < lostAt ? new Vec3(0, 0, 600)
            : t < lostAt + gap ? null
            : new Vec3(50, 0, 600);
        rig.Run(0, lostAt + gap + 2.0, 60.0, eye);

        // While the eye shows (presence above zero) it never moves faster than
        // the limit; a re-acquired track takes over only at zero presence.
        double maxSpeed = new SignalSettings().MaxSpeed;
        for (int i = 1; i < rig.Frames.Count; i++)
        {
            if (rig.Frames[i].Output.Presence <= 0 || rig.Frames[i - 1].Output.Presence <= 0)
            {
                continue;
            }
            double dt = rig.Frames[i].Time - rig.Frames[i - 1].Time;
            double step = (rig.Frames[i].Output.Eye - rig.Frames[i - 1].Output.Eye).Length;
            Assert.True(step <= maxSpeed * dt * 1.0001, $"step {step:F2} mm in {dt * 1000:F1} ms at {rig.Frames[i].Time:F3} s");
        }
        Assert.Equal(50.0, rig.Frames[^1].Output.Eye.X, 0);
    }

    [Fact]
    public void Presence_fades_out_after_the_timeout_and_back_in_when_the_face_returns()
    {
        var settings = new SignalSettings();
        var rig = new Rig(seed: 9);
        rig.Run(0, 6, 60.0, t => t < 2.0 || t >= 4.0 ? new Vec3(0, 0, 600) : null);

        double PresenceAt(double t) => rig.Frames.Last(f => f.Time <= t).Output.Presence;
        Assert.Equal(1.0, PresenceAt(2.0 + settings.PresenceTimeout - 0.05), 9);
        Assert.Equal(0.0, PresenceAt(2.0 + settings.PresenceTimeout + settings.FadeOut + 0.05), 9);
        Assert.Equal(1.0, PresenceAt(4.0 + settings.FadeIn + 0.1), 9);
        var fading = rig.Frames.Where(f => f.Time > 2.0 && f.Time < 4.0).Select(f => f.Output.Presence).ToList();
        for (int i = 1; i < fading.Count; i++)
        {
            Assert.True(fading[i] <= fading[i - 1] + 1e-12, "presence rose while the face was lost");
        }
    }

    [Fact]
    public void A_face_that_returns_mid_fade_never_jumps_the_presence()
    {
        var settings = new SignalSettings();
        var rig = new Rig(seed: 13);
        double lostAt = 2.0;
        double back = lostAt + settings.PresenceTimeout + settings.FadeOut / 2;
        rig.Run(0, back + 2, 60.0, t => t < lostAt || t >= back ? new Vec3(0, 0, 600) : null);

        double maxRise = 1.5 / settings.FadeIn; // smoothstep's steepest slope
        double maxFall = 1.5 / settings.FadeOut;
        for (int i = 1; i < rig.Frames.Count; i++)
        {
            double dt = rig.Frames[i].Time - rig.Frames[i - 1].Time;
            double change = rig.Frames[i].Output.Presence - rig.Frames[i - 1].Output.Presence;
            Assert.True(change <= maxRise * dt + 1e-9 && -change <= maxFall * dt + 1e-9, $"presence jumped by {change:F3} at {rig.Frames[i].Time:F3} s");
        }
    }

    [Fact]
    public void A_still_head_sending_identical_packets_stays_present()
    {
        // A source that keeps sending the same pose: the ring drops the
        // repeats, so only the first reaches AddSample; the rest only prove
        // the source is alive.
        var signal = new HeadSignal(new SignalSettings());
        double now = 0;
        for (int i = 0; i < 33; i++)
        {
            now = i * Period;
            signal.AddSample(now, new Vec3(i, 0, 600));
            signal.Update(now + 0.001);
        }
        HeadSignalOutput output = default;
        for (int i = 1; i <= 100; i++)
        {
            double t = now + i * Period;
            signal.MarkAlive(t);
            output = signal.Update(t + 0.001);
        }

        Assert.Equal(1.0, output.Presence, 9);
        Assert.True(output.Age < 0.01, $"age {output.Age:F3} s");
    }

    [Fact]
    public void A_head_that_stops_settles_where_it_stopped()
    {
        var signal = new HeadSignal(new SignalSettings());
        // The head moves at 100 mm/s and stops; the source then repeats the
        // last pose, and the ring drops the repeats.
        var packets = Packets(0, 4, Period, Period, t => new Vec3(t < 1.0 ? 100 * t : 100, 0, 600));

        var frames = Drive(signal, packets, 0, 4, 60);

        double last = frames[^1].Output.Eye.X;
        Assert.True(Math.Abs(last - 100) < 0.05, $"stuck at {last:F2} mm for a head stopped at 100 mm");
    }

    [Fact]
    public void Silence_still_fades_the_presence_when_nothing_arrives()
    {
        var settings = new SignalSettings();
        var signal = new HeadSignal(settings);
        for (int i = 0; i < 33; i++)
        {
            signal.AddSample(i * Period, new Vec3(0, 0, 600));
            signal.Update(i * Period + 0.001);
        }
        HeadSignalOutput output = default;
        for (double t = 1.0; t < 3.0; t += 1.0 / 60)
        {
            output = signal.Update(t);
        }

        Assert.Equal(0.0, output.Presence, 9);
    }

    [Fact]
    public void Marking_alive_before_any_sample_gives_no_eye()
    {
        var signal = new HeadSignal(new SignalSettings());

        signal.MarkAlive(1.0);
        HeadSignalOutput output = signal.Update(1.01);

        Assert.False(output.HasEye);
    }

    [Fact]
    public void Before_any_sample_there_is_no_eye()
    {
        var signal = new HeadSignal(new SignalSettings());

        HeadSignalOutput output = signal.Update(1.0);

        Assert.False(output.HasEye);
        Assert.Equal(0.0, output.Presence);
    }

    [Fact]
    public void Reset_forgets_samples_and_presence()
    {
        var rig = new Rig(seed: 17);
        rig.Run(0, 2, 60.0, t => new Vec3(10, 0, 600));

        rig.Signal.Reset();
        HeadSignalOutput output = rig.Signal.Update(2.1);

        Assert.False(output.HasEye);
        Assert.Equal(0.0, output.Presence);
    }

    [Fact]
    public void After_the_rate_drops_from_250_to_33_hz_the_period_relearns()
    {
        var signal = new HeadSignal(new SignalSettings());
        var packets = Packets(0, 3, 0.004, 0.004, t => Sway(t));
        // OpenTrack's filter switched off: still 250 packets a second, but a
        // new pose only on the tracker's frames.
        packets.AddRange(Packets(3, 8, 0.004, Period, t => Sway(t)));

        var frames = Drive(signal, packets, 0, 8, 144);

        double period = frames.Last(f => f.Time <= 5.0).Output.Period;
        Assert.True(Math.Abs(period - Period) < 0.1 * Period, $"period {period * 1000:F1} ms two seconds after the drop");
        var late = frames.Where(f => f.Time > 6).ToList();
        double held = late.Count(f => f.Output.Held) / (double)late.Count;
        Assert.True(held <= 0.01, $"held {held:P1} of frames");
        Assert.Equal(0, signal.Restarts);
    }

    [Fact]
    public void Bursts_after_receive_stalls_leave_the_period_alone()
    {
        var signal = new HeadSignal(new SignalSettings());
        var packets = Packets(0, 6, Period, Period, t => Sway(t));
        // The receive thread stalls for 150 ms every 300 ms; whatever was
        // sent meanwhile arrives at once.
        for (int i = 0; i < packets.Count; i++)
        {
            double t = packets[i].Arrival;
            double cycle = Math.Floor(t / 0.3) * 0.3;
            if (t > 1 && t - cycle < 0.15)
            {
                packets[i] = (cycle + 0.15 + i * 1e-6, packets[i].Eye, packets[i].Repeat);
            }
        }
        packets.Sort((a, b) => a.Arrival.CompareTo(b.Arrival));

        var frames = Drive(signal, packets, 0, 6, 144);

        double period = frames[^1].Output.Period;
        Assert.True(Math.Abs(period - Period) < 0.1 * Period, $"period {period * 1000:F2} ms");
        Assert.Equal(0, signal.Restarts);
    }

    [Fact]
    public void A_fast_peek_is_not_held_back_by_the_slew_limit()
    {
        var settings = new SignalSettings();
        var signal = new HeadSignal(settings);
        // A minimum-jerk lean of 150 mm in 0.3 s, peaking near 940 mm/s.
        static double Lean(double t)
        {
            double s = Math.Clamp((t - 2.0) / 0.3, 0, 1);
            return 150 * s * s * s * (10 - 15 * s + 6 * s * s);
        }
        var packets = Packets(0, 4, Period, Period, t => new Vec3(Lean(t), 0, 600));

        var frames = Drive(signal, packets, 0, 4, 144);

        double fastest = 0;
        for (int i = 1; i < frames.Count; i++)
        {
            double dt = frames[i].Time - frames[i - 1].Time;
            fastest = Math.Max(fastest, Math.Abs(frames[i].Output.Eye.X - frames[i - 1].Output.Eye.X) / dt);
        }
        Assert.True(fastest < 0.95 * settings.MaxSpeed, $"output pinned at {fastest:F0} mm/s");
        double truth = 2.0;
        while (Lean(truth) < 135)
        {
            truth += 1e-4;
        }
        double shown = frames.First(f => f.Output.Eye.X >= 135).Time;
        Assert.True(shown - truth <= 0.070, $"{(shown - truth) * 1000:F0} ms behind at 90 %");
    }

    [Fact]
    public void A_face_lost_at_a_lean_and_found_at_the_centre_does_not_sweep_the_view()
    {
        var signal = new HeadSignal(new SignalSettings());
        // A direct source sends nothing while the face is lost.
        var packets = Packets(0, 6, Period, Period,
            t => t < 2 ? new Vec3(200 + Math.Sin(7 * t), 0, 600) : t < 4 ? null : new Vec3(Math.Sin(7 * t), 0, 600));

        var frames = Drive(signal, packets, 0, 6, 60);

        foreach (var frame in frames.Where(f => f.Time >= 4))
        {
            double visible = frame.Output.Presence * frame.Output.Eye.X;
            Assert.True(Math.Abs(visible) < 2, $"a lean of {visible:F1} mm shows at {frame.Time:F2} s");
        }
    }

    [Fact]
    public void A_held_pose_that_jumps_fades_out_and_back_in_instead_of_sweeping()
    {
        var signal = new HeadSignal(new SignalSettings());
        // OpenTrack keeps sending the last pose while the face is lost, then
        // jumps to where the head is.
        var packets = Packets(0, 6, 0.004, Period,
            t => t < 2 ? new Vec3(200 + Math.Sin(7 * t), 0, 600)
                : t < 4 ? new Vec3(200 + Math.Sin(14), 0, 600)
                : new Vec3(Math.Sin(7 * t), 0, 600));

        var frames = Drive(signal, packets, 0, 6, 60);

        var after = frames.Where(f => f.Time >= 4).ToList();
        foreach (var frame in after.Where(f => f.Output.Presence > 0.01))
        {
            double x = frame.Output.Eye.X;
            Assert.True(x > 190 || x < 10, $"the eye swept through {x:F0} mm at {frame.Time:F2} s, presence {frame.Output.Presence:F2}");
        }
        Assert.True(Math.Abs(after[^1].Output.Eye.X) < 5);
        Assert.Equal(1.0, after[^1].Output.Presence, 6);
        Assert.Equal(1, signal.Restarts);
    }

    [Fact]
    public void After_a_long_pause_in_frames_the_presence_still_fades_in()
    {
        var signal = new HeadSignal(new SignalSettings());
        var packets = Packets(0, 9, Period, Period, t => t < 2 || t >= 4 ? Sway(t) : null);

        var frames = Drive(signal, packets, 0, 9, 60, framesPaused: t => t >= 3.5 && t < 7);

        var first = frames.First(f => f.Time >= 7);
        Assert.True(first.Output.Presence <= 0.2, $"presence {first.Output.Presence:F2} on the first frame after the pause");
    }
}
