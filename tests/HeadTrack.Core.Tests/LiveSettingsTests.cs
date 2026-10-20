using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

/// <summary>Stage 5: every setting can change while the camera runs, and no change jumps the view.</summary>
public class LiveSettingsTests
{
    private const double Period = 1.0 / 33.0;
    private const double Fps = 60.0;

    /// <summary>The glide time the tests allow for a settings change (s).</summary>
    private const double Glide = 0.4;

    private static CameraFrame GameCamera() => WindowProjectionTests.Camera(16, 42.6, WindowProjectionTests.GameTangents);

    private static HeadTrackSettings Absolute()
    {
        var settings = new HeadTrackSettings();
        settings.Mapper.Source = PoseSource.Absolute;
        settings.Mapper.MaxShiftX = 100;
        settings.Mapper.MaxShiftY = 100;
        return settings;
    }

    /// <summary>Feeds a 33 Hz tracker and runs 60 fps frames over [from, to).</summary>
    private static List<FrameResult> Run(HeadCoupledCamera head, double from, double to, Func<double, Vec3> eye,
        Func<double, bool>? gate = null, Func<double, bool>? freeze = null)
    {
        var frames = new List<FrameResult>();
        CameraFrame camera = GameCamera();
        double nextSample = Math.Ceiling(from / Period - 1e-9) * Period;
        int count = (int)Math.Round((to - from) * Fps);
        for (int i = 0; i < count; i++)
        {
            double now = from + i / Fps;
            while (nextSample <= now)
            {
                head.AddEye(nextSample, eye(nextSample));
                nextSample += Period;
            }
            frames.Add(head.Compute(now, camera, 1120, gate?.Invoke(now) ?? true, cut: false, null, freeze?.Invoke(now) ?? false));
        }
        return frames;
    }

    /// <summary>The same with poses, as the mod feeds them.</summary>
    private static List<FrameResult> RunPoses(HeadCoupledCamera head, double from, double to, Func<double, OpenTrackPose> pose)
    {
        var frames = new List<FrameResult>();
        CameraFrame camera = GameCamera();
        double nextSample = Math.Ceiling(from / Period - 1e-9) * Period;
        int count = (int)Math.Round((to - from) * Fps);
        for (int i = 0; i < count; i++)
        {
            double now = from + i / Fps;
            while (nextSample <= now)
            {
                head.AddPose(nextSample, pose(nextSample));
                nextSample += Period;
            }
            frames.Add(head.Compute(now, camera, 1120, gateOpen: true, cut: false));
        }
        return frames;
    }

    private static HeadCoupledCamera Centred(HeadTrackSettings settings, Vec3 rest)
    {
        var head = new HeadCoupledCamera(settings);
        head.RequestCentre(0);
        Run(head, 0, 2.0, _ => rest);
        return head;
    }

    private static Vec3 Offset(FrameResult frame) => frame.Eye - frame.Window.NeutralEye;

    /// <summary><paramref name="before"/> followed by <paramref name="after"/>: the step across a change counts.</summary>
    private static List<FrameResult> Across(FrameResult before, List<FrameResult> after)
    {
        var frames = new List<FrameResult> { before };
        frames.AddRange(after);
        return frames;
    }

    private static Vec3 Shift(FrameResult frame) => frame.Apply ? frame.View.Position - GameCamera().Position : Vec3.Zero;

    private static double LargestStep(List<FrameResult> frames, Func<FrameResult, Vec3> of)
    {
        double worst = 0;
        for (int i = 1; i < frames.Count; i++)
        {
            worst = Math.Max(worst, (of(frames[i]) - of(frames[i - 1])).Length);
        }
        return worst;
    }

    /// <summary>The largest frame step of a smoothstep glide over <paramref name="distance"/>, with 10 % slack.</summary>
    private static double GlideStep(double distance) => 1.5 * distance / Glide / Fps * 1.1;

    [Fact]
    public void A_new_smoothing_takes_effect_on_the_running_signal()
    {
        var settings = new SignalSettings();
        var signal = new HeadSignal(settings);
        var rng = new Random(3);
        double Jitter(double from, double to)
        {
            var xs = new List<double>();
            double next = from;
            for (double now = from; now < to; now += 1 / Fps)
            {
                while (next <= now)
                {
                    signal.AddSample(next, new Vec3(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1, 600));
                    next += Period;
                }
                HeadSignalOutput output = signal.Update(now);
                if (now > from + 1)
                {
                    xs.Add(output.Eye.X);
                }
            }
            double mean = xs.Average();
            return Math.Sqrt(xs.Select(x => (x - mean) * (x - mean)).Average());
        }
        double before = Jitter(0, 10);

        settings.Lateral = new OneEuroParameters(0.05, 0.0, 2.0);
        double after = Jitter(10, 20);

        Assert.True(after < 0.5 * before, $"jitter {before:F3} mm before, {after:F3} mm after a tenfold lower cutoff");
    }

    [Fact]
    public void A_new_ramp_time_applies_to_the_next_ramp()
    {
        var settings = Absolute();
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(settings, rest);
        Run(head, 2.0, 3.0, _ => rest + new Vec3(40, 0, 0));

        settings.RampSeconds = 0.1;
        List<FrameResult> closing = Run(head, 3.0, 3.2, _ => rest + new Vec3(40, 0, 0), gate: _ => false);

        Assert.False(closing[^1].Apply, $"weight {closing[^1].Weight:F3} 0.2 s into a 0.1 s ramp");
    }

    [Fact]
    public void Changing_the_gain_glides_the_view_instead_of_jumping()
    {
        var settings = Absolute();
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(settings, rest);
        FrameResult before = Run(head, 2.0, 3.0, _ => rest + new Vec3(40, 0, 0))[^1];

        settings.Mapper.Gain = 4;
        List<FrameResult> frames = Across(before, Run(head, 3.0, 4.0, _ => rest + new Vec3(40, 0, 0)));

        double step = LargestStep(frames, Offset);
        Assert.True(step <= GlideStep(120), $"the eye stepped {step:F1} mm in one frame");
        Assert.Equal(160.0, Offset(frames[^1]).X, 1);
    }

    [Fact]
    public void Changing_the_depth_gain_glides_the_view_instead_of_jumping()
    {
        var settings = Absolute();
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(settings, rest);
        FrameResult before = Run(head, 2.0, 3.0, _ => rest + new Vec3(0, 0, -60))[^1];

        settings.Mapper.DepthGain = 2;
        List<FrameResult> frames = Across(before, Run(head, 3.0, 4.0, _ => rest + new Vec3(0, 0, -60)));

        double step = LargestStep(frames, Offset);
        Assert.True(step <= GlideStep(Math.Abs(Offset(before).Z)), $"the eye stepped {step:F1} mm in one frame");
        Assert.True(Offset(frames[^1]).Z < 1.8 * Offset(before).Z, $"depth offset {Offset(frames[^1]).Z:F1} mm at depth gain 2");
    }

    [Fact]
    public void Changing_the_window_depth_glides_the_camera_instead_of_jumping()
    {
        var settings = Absolute();
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(settings, rest);
        FrameResult before = Run(head, 2.0, 3.0, _ => rest + new Vec3(40, 0, 0))[^1];
        double moved = Shift(before).Length;

        settings.WindowDepthFraction = 0.5;
        List<FrameResult> frames = Across(before, Run(head, 3.0, 4.0, _ => rest + new Vec3(40, 0, 0)));

        double step = LargestStep(frames, Shift);
        Assert.True(step <= GlideStep(moved / 2), $"the camera stepped {step:F2} m in one frame (shift {moved:F2} m)");
        Assert.Equal(0.5, frames[^1].Window.Scale / before.Window.Scale, 9);
    }

    [Fact]
    public void Inverting_an_axis_glides_the_view_through_the_centre()
    {
        var settings = Absolute();
        var rest = new Vec3(0, 0, 600);
        var head = new HeadCoupledCamera(settings);
        head.RequestCentre(0);
        RunPoses(head, 0, 2.0, _ => new OpenTrackPose(0, 0, 60, 0, 0, 0));
        FrameResult before = RunPoses(head, 2.0, 3.0, _ => new OpenTrackPose(-4, 0, 60, 0, 0, 0))[^1];

        settings.Mapper.InvertX = true;
        List<FrameResult> frames = Across(before, RunPoses(head, 3.0, 4.0, _ => new OpenTrackPose(-4, 0, 60, 0, 0, 0)));

        double step = LargestStep(frames, Offset);
        Assert.True(step <= GlideStep(80), $"the eye stepped {step:F1} mm in one frame");
        Assert.Equal(-40.0, Offset(frames[^1]).X, 2);
    }

    [Fact]
    public void A_new_viewing_distance_keeps_a_centred_head_at_the_neutral_eye()
    {
        // OpenTrack's centred output, centred once more by the mod (its own
        // key: OpenTrack's hotkeys are dead under Wayland).
        var settings = new HeadTrackSettings();
        var head = new HeadCoupledCamera(settings);
        head.RequestCentre(0);
        RunPoses(head, 0, 2.0, _ => new OpenTrackPose(0, 0, 0, 0, 0, 0));

        settings.Mapper.RelativeDistanceMm = 700;
        List<FrameResult> frames = RunPoses(head, 2.0, 4.0, _ => new OpenTrackPose(0, 0, 0, 0, 0, 0));

        Assert.False(frames[^1].Apply, $"a centred head maps {Offset(frames[^1]).Length:F1} mm off neutral");
    }

    [Fact]
    public void Switching_the_mode_glides_the_view_instead_of_jumping()
    {
        var settings = Absolute();
        var rest = new Vec3(-31.5, 84, 571.5);
        HeadCoupledCamera head = Centred(settings, rest);
        FrameResult before = Run(head, 2.0, 3.0, _ => rest)[^1];

        settings.Mapper.Mode = EyeMode.Window;
        List<FrameResult> frames = Across(before, Run(head, 3.0, 4.0, _ => rest));

        double distance = (rest - frames[^1].Window.NeutralEye).Length;
        double step = LargestStep(frames, Offset);
        Assert.True(step <= GlideStep(distance), $"the eye stepped {step:F1} mm in one frame");
        Assert.True((frames[^1].Eye - rest).Length < 1e-6, "window mode at rest shows the true eye");
    }

    [Fact]
    public void Switching_to_an_absolute_source_takes_a_centre_by_itself()
    {
        var settings = new HeadTrackSettings();
        var head = new HeadCoupledCamera(settings);
        RunPoses(head, 0, 2.0, _ => new OpenTrackPose(0, 0, 0, 0, 0, 0));

        // OpenTrack stops; tobii-opentrack starts a second later.
        settings.Mapper.Source = PoseSource.Absolute;
        var camera = GameCamera();
        for (double now = 2.0; now < 3.0; now += 1 / Fps)
        {
            head.Compute(now, camera, 1120, gateOpen: true, cut: false);
        }
        RunPoses(head, 3.0, 6.0, _ => new OpenTrackPose(3.15, 8.4, 57.15, 0, 0, 0));
        List<FrameResult> moved = RunPoses(head, 6.0, 8.0, _ => new OpenTrackPose(3.15 - 4, 8.4, 57.15, 0, 0, 0));

        Assert.True(head.Mapper.HasReference);
        Assert.Equal(40.0, Offset(moved[^1]).X, 0);
    }

    [Fact]
    public void Switching_to_a_relative_source_forgets_an_absolute_centre()
    {
        var settings = new HeadTrackSettings();
        settings.Mapper.Source = PoseSource.Absolute;
        var head = new HeadCoupledCamera(settings);
        head.RequestCentre(0);
        RunPoses(head, 0, 3.0, _ => new OpenTrackPose(3.15, 8.4, 57.15, 0, 0, 0));

        settings.Mapper.Source = PoseSource.Relative;
        var camera = GameCamera();
        for (double now = 3.0; now < 4.0; now += 1 / Fps)
        {
            head.Compute(now, camera, 1120, gateOpen: true, cut: false);
        }
        List<FrameResult> frames = RunPoses(head, 4.0, 7.0, _ => new OpenTrackPose(0, 0, 0, 0, 0, 0));

        Assert.False(frames[^1].Apply, $"OpenTrack's centre maps {Offset(frames[^1]).Length:F1} mm off neutral");
    }

    [Fact]
    public void A_zero_centre_blend_never_gives_a_nan_eye()
    {
        var settings = new MapperSettings { Source = PoseSource.Absolute, CentreBlendSeconds = 0 };
        var mapper = new EyeMapper(settings);
        var signal = new HeadSignalOutput(true, new Vec3(10, 0, 600), 1.0, 0.0, Period, 0.035, false, false);
        mapper.RequestCentre(0);

        for (int i = 0; i <= 60; i++)
        {
            Vec3 eye = mapper.Map(i / 60.0, signal, 497, new Vec3(0, 0, 497));
            Assert.True(eye.IsFinite, $"frame {i}: {eye}");
        }
    }

    [Fact]
    public void A_deep_lean_at_depth_gain_3_approaches_the_depth_limit_without_a_jump()
    {
        var settings = new MapperSettings { Source = PoseSource.Absolute, DepthGain = 3 };
        var mapper = new EyeMapper(settings);
        var neutral = new Vec3(0, 0, 497);
        HeadSignalOutput At(double z) => new(true, new Vec3(0, 0, z), 1.0, 0.0, Period, 0.035, false, false);
        mapper.RequestCentre(0);
        for (int i = 0; i <= 60; i++)
        {
            mapper.Map(i / 60.0, At(600), 497, neutral);
        }
        double now = 2.0;
        double previous = mapper.Map(now, At(600), 497, neutral).Z;

        for (double z = 599; z >= 60; z -= 1)
        {
            double mapped = mapper.Map(now, At(z), 497, neutral).Z;
            Assert.True(mapped <= previous + 1e-9, $"the eye moved back from {previous:F1} to {mapped:F1} mm as the head came to {z} mm");
            Assert.True(previous - mapped < 5, $"the eye jumped {previous - mapped:F1} mm at {z} mm");
            previous = mapped;
        }
        Assert.True(previous >= settings.MinDepthRatio * 497 - 1e-9);
    }

    [Fact]
    public void Window_mode_limits_are_measured_from_the_rest_pose()
    {
        var settings = new MapperSettings { Source = PoseSource.Absolute, Mode = EyeMode.Window };
        var mapper = new EyeMapper(settings);
        var neutral = new Vec3(0, 0, 497);
        var rest = new Vec3(-31.5, 84, 700);
        HeadSignalOutput At(Vec3 eye) => new(true, eye, 1.0, 0.0, Period, 0.035, false, false);
        mapper.RequestCentre(0);
        for (int i = 0; i <= 60; i++)
        {
            mapper.Map(i / 60.0, At(rest), 497, neutral);
        }
        double now = 2.0;

        Vec3 still = mapper.Map(now, At(rest), 497, neutral);
        double right = mapper.Map(now, At(rest + new Vec3(250, 0, 0)), 497, neutral).X - rest.X;
        double left = rest.X - mapper.Map(now, At(rest - new Vec3(250, 0, 0)), 497, neutral).X;

        Assert.True((still - rest).Length < 1e-6, $"the rest pose maps to {still}");
        Assert.Equal(right, left, 6);
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(4.0)]
    public void At_gain_g_the_view_follows_a_head_track_as_gain_1_follows_g_times_that_track(double gain)
    {
        HeadCoupledCamera Make(double g)
        {
            var settings = Absolute();
            settings.Mapper.Gain = g;
            return Centred(settings, new Vec3(0, 0, 600));
        }
        var rng = new Random(5);
        var noise = Enumerable.Range(0, 400).Select(_ => new Vec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, 0)).ToArray();
        Vec3 Track(double t) => new Vec3(15 * Math.Sin(2 * Math.PI * 0.7 * t), 6 * Math.Sin(2 * Math.PI * 0.4 * t), 0)
            + noise[(int)Math.Round(t / Period) % noise.Length];
        HeadCoupledCamera gained = Make(gain);
        HeadCoupledCamera scaled = Make(1.0);

        List<FrameResult> a = Run(gained, 2.0, 6.0, t => new Vec3(0, 0, 600) + Track(t));
        List<FrameResult> b = Run(scaled, 2.0, 6.0, t => new Vec3(0, 0, 600) + Track(t) * gain);

        double worst = a.Zip(b, (x, y) => (Offset(x) - Offset(y)).Length).Max();
        Assert.True(worst < 1e-6, $"the views differ by up to {worst:F3} mm");
    }

    [Fact]
    public void A_change_while_frozen_waits_for_the_release_and_then_glides()
    {
        var settings = Absolute();
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(settings, rest);
        FrameResult before = Run(head, 2.0, 3.0, _ => rest + new Vec3(40, 0, 0))[^1];

        settings.Mapper.Gain = 3;
        List<FrameResult> frozen = Run(head, 3.0, 4.0, _ => rest + new Vec3(40, 0, 0), freeze: _ => true);
        List<FrameResult> released = Across(frozen[^1], Run(head, 4.0, 5.0, _ => rest + new Vec3(40, 0, 0)));

        Assert.True(frozen.All(f => (Offset(f) - Offset(before)).Length < 1e-9), "the view moved while frozen");
        double step = LargestStep(released, Offset);
        double bound = 1.5 * 80 / settings.FreezeReleaseSeconds / Fps * 1.1;
        Assert.True(step <= bound, $"the eye stepped {step:F1} mm in one frame");
        Assert.Equal(120.0, Offset(released[^1]).X, 1);
    }

    [Fact]
    public void A_gain_cap_while_a_tool_is_active_glides_down_and_back()
    {
        var settings = Absolute();
        settings.Mapper.Gain = 3;
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(settings, rest);
        FrameResult before = Run(head, 2.0, 3.0, _ => rest + new Vec3(30, 0, 0))[^1];
        CameraFrame camera = GameCamera();
        var frames = new List<FrameResult> { before };
        double next = Math.Ceiling(3.0 / Period - 1e-9) * Period;
        for (int i = 0; i < 180; i++)
        {
            double now = 3.0 + i / Fps;
            while (next <= now)
            {
                head.AddEye(next, rest + new Vec3(30, 0, 0));
                next += Period;
            }
            bool tool = now >= 3.0 && now < 4.0;
            frames.Add(head.Compute(now, camera, 1120, gateOpen: true, cut: false, maxGain: tool ? 1.0 : double.PositiveInfinity));
        }

        double step = LargestStep(frames, Offset);
        Assert.True(step <= GlideStep(60), $"the eye stepped {step:F1} mm in one frame");
        Assert.Equal(30.0, Offset(frames[(int)(0.95 * Fps)]).X, 1);
        Assert.Equal(90.0, Offset(frames[^1]).X, 1);
    }
}
