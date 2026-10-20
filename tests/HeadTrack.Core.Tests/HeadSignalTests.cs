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

        double maxSpeed = new SignalSettings().MaxSpeed;
        for (int i = 1; i < rig.Frames.Count; i++)
        {
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
    public void A_face_that_returns_mid_fade_resumes_from_the_current_presence()
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
        Vec3 halfway = default;
        for (int i = 1; i <= 100; i++)
        {
            double t = now + i * Period;
            signal.MarkAlive(t);
            output = signal.Update(t + 0.001);
            if (i == 50)
            {
                halfway = output.Eye;
            }
        }

        Assert.Equal(1.0, output.Presence, 9);
        Assert.True(output.Age < 0.01, $"age {output.Age:F3} s");
        Assert.True((output.Eye - halfway).Length < 1e-9, $"drifted {(output.Eye - halfway).Length:F3} mm while still");
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
}
