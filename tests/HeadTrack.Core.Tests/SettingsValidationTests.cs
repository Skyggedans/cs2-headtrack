using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class SettingsValidationTests
{
    [Fact]
    public void Default_settings_need_no_correction()
    {
        var settings = new HeadTrackSettings();

        IReadOnlyList<string> corrections = settings.Validate();

        Assert.Empty(corrections);
    }

    [Fact]
    public void A_zero_gap_is_raised_so_not_every_frame_is_a_cut()
    {
        var settings = new HeadTrackSettings { GapSeconds = 0 };
        settings.Mapper.Source = PoseSource.Absolute;

        IReadOnlyList<string> corrections = settings.Validate();
        var head = new HeadCoupledCamera(settings);
        head.RequestCentre(0);
        CameraFrame camera = WindowProjectionTests.Camera(16, 42.6, WindowProjectionTests.GameTangents);
        FrameResult result = default;
        for (int i = 0; i < 180; i++)
        {
            double now = i / 60.0;
            head.AddEye(now, new Vec3(now < 1.5 ? 0 : 40, 0, 600));
            result = head.Compute(now, camera, 1120, gateOpen: true, cut: false);
        }

        Assert.Equal(SettingsLimits.GapSecondsMin, settings.GapSeconds);
        Assert.Single(corrections);
        Assert.True(result.Apply, "a head 40 mm off its centre moves nothing");
    }

    [Fact]
    public void What_is_not_a_number_becomes_the_default()
    {
        var settings = new HeadTrackSettings();
        settings.Mapper.Gain = double.NaN;
        settings.Signal.FadeIn = double.PositiveInfinity;

        settings.Validate();

        Assert.Equal(1.0, settings.Mapper.Gain);
        Assert.Equal(new SignalSettings().FadeIn, settings.Signal.FadeIn);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(99.0, SettingsLimits.SecondsMax)]
    public void Durations_that_may_be_zero_are_kept_between_zero_and_the_maximum(double value, double expected)
    {
        var settings = new HeadTrackSettings { RampSeconds = value, GlideSeconds = value, FreezeReleaseSeconds = value };
        settings.Mapper.CentreBlendSeconds = value;
        settings.Signal.FadeOut = value;

        settings.Validate();

        Assert.Equal(expected, settings.RampSeconds);
        Assert.Equal(expected, settings.GlideSeconds);
        Assert.Equal(expected, settings.FreezeReleaseSeconds);
        Assert.Equal(expected, settings.Mapper.CentreBlendSeconds);
        Assert.Equal(expected, settings.Signal.FadeOut);
    }

    [Fact]
    public void Gains_and_the_window_depth_are_kept_on_the_sliders()
    {
        var settings = new HeadTrackSettings { WindowDepthFraction = -2 };
        settings.Mapper.Gain = 50;
        settings.Mapper.DepthGain = -1;

        settings.Validate();

        Assert.Equal(SettingsLimits.GainMax, settings.Mapper.Gain);
        Assert.Equal(SettingsLimits.DepthGainMin, settings.Mapper.DepthGain);
        Assert.Equal(SettingsLimits.WindowDepthMin, settings.WindowDepthFraction);
    }

    [Fact]
    public void The_depth_limits_stay_on_either_side_of_the_rest_distance()
    {
        var settings = new HeadTrackSettings();
        settings.Mapper.MinDepthRatio = 1.2;
        settings.Mapper.MaxDepthRatio = 0.8;

        settings.Validate();

        Assert.True(settings.Mapper.MinDepthRatio < 1 && settings.Mapper.MaxDepthRatio > 1);
    }

    [Fact]
    public void The_period_bounds_stay_in_order()
    {
        var settings = new HeadTrackSettings();
        settings.Signal.MinPeriod = 0.08;
        settings.Signal.MaxPeriod = 0.01;

        settings.Validate();

        Assert.True(settings.Signal.MinPeriod < settings.Signal.MaxPeriod);
        Assert.InRange(settings.Signal.NominalPeriod, settings.Signal.MinPeriod, settings.Signal.MaxPeriod);
    }

    [Fact]
    public void A_filter_cutoff_must_be_positive_and_off_is_allowed()
    {
        var settings = new HeadTrackSettings();
        settings.Signal.Lateral = new OneEuroParameters(-1, -0.5, 0);
        settings.Signal.Depth = OneEuroParameters.Off;

        settings.Validate();

        Assert.True(settings.Signal.Lateral.MinCutoff > 0 && settings.Signal.Lateral.Beta >= 0 && settings.Signal.Lateral.DCutoff > 0);
        Assert.True(double.IsPositiveInfinity(settings.Signal.Depth.MinCutoff));
    }
}

public class SmoothingTests
{
    private const double Dt = 1.0 / 33.0;

    [Fact]
    public void Half_way_is_the_default_filter()
    {
        OneEuroParameters lateral = Smoothing.Lateral(Smoothing.Default);
        OneEuroParameters depth = Smoothing.Depth(Smoothing.Default);
        var defaults = new SignalSettings();

        Assert.Equal(defaults.Lateral.MinCutoff, lateral.MinCutoff, 12);
        Assert.Equal(defaults.Lateral.Beta, lateral.Beta, 12);
        Assert.Equal(defaults.Lateral.DCutoff, lateral.DCutoff, 12);
        Assert.Equal(defaults.Depth.MinCutoff, depth.MinCutoff, 12);
        Assert.Equal(defaults.Depth.Beta, depth.Beta, 12);
    }

    [Fact]
    public void Zero_turns_the_filter_off()
    {
        var filter = new EyeFilter(Smoothing.Lateral(0), Smoothing.Depth(0));
        var rng = new Random(2);
        for (int i = 0; i < 100; i++)
        {
            var x = new Vec3(rng.NextDouble() * 10, rng.NextDouble() * 10, 600 + rng.NextDouble() * 10);

            Vec3 y = filter.Filter(x, Dt);

            Assert.True((y - x).Length < 1e-9, $"{y} for {x}");
        }
    }

    [Fact]
    public void More_smoothing_lowers_the_cutoff_at_rest_and_keeps_the_speed_response()
    {
        for (int percent = 1; percent < Smoothing.Max; percent++)
        {
            OneEuroParameters less = Smoothing.Lateral(percent);
            OneEuroParameters more = Smoothing.Lateral(percent + 1);

            Assert.True(more.MinCutoff < less.MinCutoff, $"at {percent} %");
            Assert.Equal(less.Beta, more.Beta);
        }
    }

    [Theory]
    [InlineData("4242", true)]
    [InlineData(" 4243 ", true)]
    [InlineData("80", false)]
    [InlineData("70000", false)]
    [InlineData("42a", false)]
    [InlineData("", false)]
    public void A_port_is_a_number_from_1024_to_65535(string text, bool valid)
    {
        bool parsed = SettingsLimits.TryParsePort(text, out int port, out string? error);

        Assert.Equal(valid, parsed);
        Assert.Equal(valid, error == null);
        if (valid)
        {
            Assert.Equal(int.Parse(text.Trim()), port);
        }
    }
}
