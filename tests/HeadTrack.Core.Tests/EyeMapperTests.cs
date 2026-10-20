using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class EyeMapperTests
{
    private const double Z0 = 497.0;
    private static readonly Vec3 Neutral = new(0, 0, Z0);

    private static HeadSignalOutput Signal(Vec3 eye, double presence = 1.0) =>
        new(true, eye, presence, 0.0, 1.0 / 33.0, 0.035, false);

    /// <summary>A mapper whose centre is captured from <paramref name="rest"/>.</summary>
    private static (EyeMapper Mapper, double Now) Centred(MapperSettings settings, Vec3 rest)
    {
        var mapper = new EyeMapper(settings);
        double now = 0;
        mapper.RequestCentre(now);
        for (int i = 0; i <= 60; i++)
        {
            now = i / 60.0;
            mapper.Map(now, Signal(rest), Z0, Neutral);
        }
        now += settings.CentreBlendSeconds + 0.1;
        mapper.Map(now, Signal(rest), Z0, Neutral);
        return (mapper, now);
    }

    private static MapperSettings Unsaturated() => new() { MaxShiftX = 100, MaxShiftY = 100, MinDepthRatio = 0.01, MaxDepthRatio = 100 };

    [Fact]
    public void An_absolute_pose_gives_the_display_frame_eye()
    {
        var mapper = new EyeMapper(new MapperSettings { Source = PoseSource.Absolute });

        Vec3 eye = mapper.SourceEye(new OpenTrackPose(3.15, 8.4, 57.15, 0, 0, 0));

        Assert.Equal(-31.5, eye.X, 9);
        Assert.Equal(84.0, eye.Y, 9);
        Assert.Equal(571.5, eye.Z, 9);
    }

    [Fact]
    public void A_relative_pose_is_an_offset_from_the_configured_viewing_distance()
    {
        var mapper = new EyeMapper(new MapperSettings { Source = PoseSource.Relative, RelativeDistanceMm = 650 });

        Vec3 eye = mapper.SourceEye(new OpenTrackPose(-1.0, 2.0, -3.0, 0, 0, 0));

        Assert.Equal(10.0, eye.X, 9);
        Assert.Equal(20.0, eye.Y, 9);
        Assert.Equal(620.0, eye.Z, 9);
    }

    [Fact]
    public void Inverted_axes_flip_their_offsets()
    {
        var mapper = new EyeMapper(new MapperSettings { Source = PoseSource.Relative, RelativeDistanceMm = 600, InvertX = true, InvertY = true, InvertZ = true });

        Vec3 eye = mapper.SourceEye(new OpenTrackPose(-1.0, 2.0, -3.0, 0, 0, 0));

        Assert.Equal(-10.0, eye.X, 9);
        Assert.Equal(-20.0, eye.Y, 9);
        Assert.Equal(630.0, eye.Z, 9);
    }

    [Fact]
    public void Centred_the_captured_rest_pose_maps_to_the_neutral_eye()
    {
        var (mapper, now) = Centred(Unsaturated(), new Vec3(-31.5, 84, 571.5));

        Vec3 eye = mapper.Map(now, Signal(new Vec3(-31.5, 84, 571.5)), Z0, Neutral);

        Assert.Equal(0.0, eye.X, 6);
        Assert.Equal(0.0, eye.Y, 6);
        Assert.Equal(Z0, eye.Z, 6);
    }

    [Fact]
    public void Centred_lateral_offsets_are_scaled_by_the_gain()
    {
        var settings = Unsaturated();
        settings.Gain = 3.0;
        var rest = new Vec3(-31.5, 84, 571.5);
        var (mapper, now) = Centred(settings, rest);

        Vec3 eye = mapper.Map(now, Signal(rest + new Vec3(50, -20, 0)), Z0, Neutral);

        Assert.Equal(150.0, eye.X, 6);
        Assert.Equal(-60.0, eye.Y, 6);
    }

    [Fact]
    public void Centred_depth_is_proportional_so_leaning_keeps_the_far_field_size()
    {
        var rest = new Vec3(0, 0, 600);
        var (mapper, now) = Centred(Unsaturated(), rest);

        Vec3 eye = mapper.Map(now, Signal(new Vec3(0, 0, 660)), Z0, Neutral);

        Assert.Equal(Z0 * 1.1, eye.Z, 6);
    }

    [Fact]
    public void Window_mode_at_unit_gain_passes_the_true_eye()
    {
        var settings = Unsaturated();
        settings.Mode = EyeMode.Window;
        var mapper = new EyeMapper(settings);

        Vec3 eye = mapper.Map(1.0, Signal(new Vec3(-31.5, 84, 571.5)), Z0, Neutral);

        Assert.Equal(-31.5, eye.X, 6);
        Assert.Equal(84.0, eye.Y, 6);
        Assert.Equal(571.5, eye.Z, 6);
    }

    [Fact]
    public void Window_mode_gain_amplifies_motion_around_the_rest_pose()
    {
        var settings = Unsaturated();
        settings.Mode = EyeMode.Window;
        settings.Gain = 3.0;
        var rest = new Vec3(-31.5, 84, 571.5);
        var (mapper, now) = Centred(settings, rest);

        Vec3 eye = mapper.Map(now, Signal(rest + new Vec3(10, 0, 0)), Z0, Neutral);

        Assert.Equal(-31.5 + 30.0, eye.X, 6);
        Assert.Equal(84.0, eye.Y, 6);
    }

    [Fact]
    public void Without_presence_the_centred_eye_returns_to_neutral()
    {
        var rest = new Vec3(0, 0, 600);
        var (mapper, now) = Centred(Unsaturated(), rest);

        Vec3 eye = mapper.Map(now, Signal(new Vec3(80, 40, 650), presence: 0.0), Z0, Neutral);

        Assert.Equal(0.0, eye.X, 9);
        Assert.Equal(0.0, eye.Y, 9);
        Assert.Equal(Z0, eye.Z, 9);
    }

    [Fact]
    public void Half_presence_gives_half_the_offset()
    {
        var rest = new Vec3(0, 0, 600);
        var (mapper, now) = Centred(Unsaturated(), rest);

        Vec3 eye = mapper.Map(now, Signal(new Vec3(80, 0, 600), presence: 0.5), Z0, Neutral);

        Assert.Equal(40.0, eye.X, 6);
    }

    [Fact]
    public void Lateral_shift_saturates_softly_below_the_limit()
    {
        var settings = new MapperSettings { MaxShiftX = 0.6, MaxShiftY = 0.45 };
        var rest = new Vec3(0, 0, 600);
        var (mapper, now) = Centred(settings, rest);
        double previous = 0;

        foreach (double x in new[] { 10.0, 100.0, 300.0, 600.0, 3000.0 })
        {
            Vec3 eye = mapper.Map(now, Signal(new Vec3(x, 0, 600)), Z0, Neutral);
            double shift = eye.X / settings.ScreenWidthMm;
            Assert.True(shift < settings.MaxShiftX, $"shift {shift:F3} at {x} mm");
            Assert.True(shift > previous, "saturation is not monotonic");
            previous = shift;
        }
        Vec3 small = mapper.Map(now, Signal(new Vec3(10, 0, 600)), Z0, Neutral);
        Assert.Equal(10.0, small.X, 1);
    }

    [Fact]
    public void Depth_ratio_is_kept_within_its_limits()
    {
        var settings = new MapperSettings();
        var rest = new Vec3(0, 0, 600);
        var (mapper, now) = Centred(settings, rest);

        Vec3 near = mapper.Map(now, Signal(new Vec3(0, 0, 100)), Z0, Neutral);
        Vec3 far = mapper.Map(now, Signal(new Vec3(0, 0, 5000)), Z0, Neutral);

        Assert.True(near.Z / Z0 >= settings.MinDepthRatio - 1e-9, $"near ratio {near.Z / Z0:F3}");
        Assert.True(far.Z / Z0 <= settings.MaxDepthRatio + 1e-9, $"far ratio {far.Z / Z0:F3}");
    }

    [Fact]
    public void Re_centring_glides_instead_of_jumping()
    {
        var settings = Unsaturated();
        var rest = new Vec3(0, 0, 600);
        var (mapper, now) = Centred(settings, rest);
        var leaning = new Vec3(60, 0, 600);
        Vec3 before = mapper.Map(now, Signal(leaning), Z0, Neutral);
        Assert.Equal(60.0, before.X, 6);

        mapper.RequestCentre(now);
        double worst = 0;
        Vec3 previous = before;
        for (int i = 1; i <= 120; i++)
        {
            Vec3 eye = mapper.Map(now + i / 60.0, Signal(leaning), Z0, Neutral);
            worst = Math.Max(worst, Math.Abs(eye.X - previous.X));
            previous = eye;
        }

        Assert.Equal(0.0, previous.X, 6);
        Assert.True(worst < 5.0, $"largest step {worst:F2} mm");
    }

    [Fact]
    public void Before_a_centre_exists_the_centred_eye_is_neutral()
    {
        var mapper = new EyeMapper(new MapperSettings());

        Vec3 eye = mapper.Map(0.0, Signal(new Vec3(80, 40, 650)), Z0, Neutral);

        Assert.False(mapper.HasReference);
        Assert.Equal(0.0, eye.X, 9);
        Assert.Equal(Z0, eye.Z, 9);
    }
}
