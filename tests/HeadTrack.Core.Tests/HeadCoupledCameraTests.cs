using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class HeadCoupledCameraTests
{
    private const double Period = 1.0 / 33.0;

    private static CameraFrame GameCamera() => WindowProjectionTests.Camera(16, 42.6, WindowProjectionTests.GameTangents);

    /// <summary>Feeds the tracker at 33 Hz and runs frames at 60 fps from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static FrameResult Run(HeadCoupledCamera head, double from, double to, Func<double, Vec3?> eye,
        Func<Vec3, Vec3>? clamp = null, Func<double, bool>? gate = null, double depth = 1120,
        Func<double, bool>? freeze = null, List<FrameResult>? frames = null)
    {
        CameraFrame camera = GameCamera();
        FrameResult result = default;
        double nextSample = Math.Ceiling(from / Period) * Period;
        for (double now = from; now < to; now += 1.0 / 60.0)
        {
            while (nextSample <= now)
            {
                Vec3? value = eye(nextSample);
                if (value.HasValue)
                {
                    head.AddEye(nextSample, value.Value);
                }
                nextSample += Period;
            }
            result = head.Compute(now, camera, depth, gate?.Invoke(now) ?? true, cut: false, clamp, freeze?.Invoke(now) ?? false);
            frames?.Add(result);
        }
        return result;
    }

    private static HeadCoupledCamera Centred(Vec3 rest)
    {
        var head = new HeadCoupledCamera(new HeadTrackSettings());
        head.RequestCentre(0);
        Run(head, 0, 2.0, t => rest);
        return head;
    }

    [Fact]
    public void Without_samples_nothing_is_written()
    {
        var head = new HeadCoupledCamera(new HeadTrackSettings());

        FrameResult result = head.Compute(1.0, GameCamera(), 1120, gateOpen: true, cut: false);

        Assert.False(result.Apply);
    }

    [Fact]
    public void A_head_at_its_centre_writes_nothing_so_the_game_stays_bit_exact()
    {
        var rest = new Vec3(-31.5, 84, 571.5);
        HeadCoupledCamera head = Centred(rest);

        FrameResult result = Run(head, 2.0, 3.0, t => rest);

        Assert.False(result.Apply);
    }

    [Fact]
    public void Moving_the_head_right_moves_the_camera_right_by_the_window_scale()
    {
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(rest);
        CameraFrame camera = GameCamera();

        FrameResult result = Run(head, 2.0, 4.0, t => rest + new Vec3(50, 0, 0));

        Assert.True(result.Apply);
        Assert.True(result.View.Valid);
        double moved = Vec3.Dot(result.View.Position - camera.Position, camera.Right);
        double expected = 50 * result.Window.Scale;
        Assert.True(Math.Abs(moved - expected) < 0.01, $"moved {moved:F4} m, expected {expected:F4} m");
    }

    [Fact]
    public void Closing_the_gate_ramps_the_effect_out_and_then_writes_nothing()
    {
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(rest);
        Run(head, 2.0, 3.0, t => rest + new Vec3(50, 0, 0));

        FrameResult closing = Run(head, 3.0, 3.2, t => rest + new Vec3(50, 0, 0), gate: _ => false);
        FrameResult closed = Run(head, 3.2, 4.0, t => rest + new Vec3(50, 0, 0), gate: _ => false);

        Assert.True(closing.Apply);
        Assert.True(closing.Weight > 0 && closing.Weight < 1);
        Assert.False(closed.Apply);
    }

    [Fact]
    public void A_cut_stops_writing_at_once()
    {
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(rest);
        Run(head, 2.0, 3.0, t => rest + new Vec3(50, 0, 0));

        FrameResult result = head.Compute(3.0, GameCamera(), 1120, gateOpen: true, cut: true);

        Assert.False(result.Apply);
        Assert.Equal(0.0, result.Weight);
    }

    [Fact]
    public void A_clamped_position_is_used_and_still_frames_the_window()
    {
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(rest);
        Vec3 Lift(Vec3 p) => p + new Vec3(0, 25, 0);

        FrameResult result = Run(head, 2.0, 4.0, t => rest + new Vec3(0, -80, 0), clamp: Lift);

        Assert.True(result.Apply);
        Assert.True(result.Clamped);
        Assert.Equal(25.0, (result.View.Position - result.Unclamped).Y, 9);
    }

    [Fact]
    public void An_offset_that_would_give_no_view_is_shrunk_until_it_does()
    {
        var settings = new HeadTrackSettings();
        settings.Mapper.MaxShiftY = 100;
        settings.Mapper.MinDepthRatio = 0.001;
        var head = new HeadCoupledCamera(settings);
        head.RequestCentre(0);
        var rest = new Vec3(0, 0, 600);
        Run(head, 0, 2.0, t => rest);
        // A huge lift that would put the eye far above the window.
        Vec3 Lift(Vec3 p) => p + new Vec3(0, 50000, 0);

        FrameResult result = Run(head, 2.0, 4.0, t => rest + new Vec3(0, 0, -100), clamp: Lift);

        Assert.True(!result.Apply || result.View.Valid);
    }

    [Fact]
    public void Zooming_with_the_head_off_centre_keeps_the_pivot_still_on_screen()
    {
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(rest);
        Run(head, 2.0, 3.0, t => rest + new Vec3(50, 0, 0));
        CameraFrame start = GameCamera();
        Vec3 pivot = start.Position + start.Forward * 1000;
        double worst = 0;
        for (int i = 0; i < 60; i++)
        {
            // The game zooms from 1000 m to 100 m over a second.
            double zoom = 1000 - 900 * i / 59.0;
            var camera = new CameraFrame(pivot - start.Forward * zoom, start.Right, start.Up, start.Forward, start.Near, start.Far, start.Tangents);
            FrameResult result = head.Compute(3.0 + i / 60.0, camera, zoom, gateOpen: true, cut: false);
            Assert.True(result.Apply);
            (double x, double y) = WindowProjectionTests.Ndc(result.View.Matrix, result.View.Position, camera, pivot);
            worst = Math.Max(worst, Math.Max(Math.Abs(x), Math.Abs(y)));
        }

        Assert.True(worst < 2.0 / 3840, $"the pivot moved {worst * 1920:F1} px");
    }

    [Fact]
    public void After_frames_without_compute_the_effect_ramps_in_again()
    {
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(rest);
        Run(head, 2.0, 3.0, t => rest + new Vec3(50, 0, 0));
        var frames = new List<FrameResult>();

        // Loading: 30 s without a frame.
        Run(head, 33.0, 33.5, t => rest + new Vec3(50, 0, 0), frames: frames);

        Assert.True(frames[1].Weight < 0.05, $"weight {frames[1].Weight:F3} on the second frame back");
    }

    [Fact]
    public void After_a_held_cut_the_effect_ramps_in()
    {
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(rest);
        Run(head, 2.0, 3.0, t => rest + new Vec3(50, 0, 0));
        CameraFrame camera = GameCamera();
        for (int i = 0; i < 60; i++)
        {
            head.Compute(3.0 + i / 60.0, camera, 1120, gateOpen: true, cut: true);
        }

        FrameResult first = head.Compute(4.0, camera, 1120, gateOpen: true, cut: false);

        Assert.True(first.Weight < 0.05, $"weight {first.Weight:F3} on the first frame after the cut");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void A_bad_depth_for_one_frame_does_not_end_the_effect(double bad)
    {
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(rest);
        Run(head, 2.0, 3.0, t => rest + new Vec3(50, 0, 0));
        head.Compute(3.0, GameCamera(), bad, gateOpen: true, cut: false);

        FrameResult after = Run(head, 3.0 + 1.0 / 60, 3.5, t => rest + new Vec3(50, 0, 0));

        Assert.True(after.Apply);
        Assert.True(after.View.Valid);
    }

    [Fact]
    public void A_nan_time_writes_nothing_and_leaves_the_state_alone()
    {
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(rest);
        FrameResult before = Run(head, 2.0, 3.0, t => rest + new Vec3(50, 0, 0));

        FrameResult nan = head.Compute(double.NaN, GameCamera(), 1120, gateOpen: true, cut: false);
        FrameResult after = Run(head, 3.0, 3.1, t => rest + new Vec3(50, 0, 0));

        Assert.False(nan.Apply);
        Assert.True(after.Apply);
        Assert.Equal(before.Weight, after.Weight, 9);
    }

    [Fact]
    public void While_frozen_the_camera_stays_where_it_was_when_the_freeze_began()
    {
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(rest);
        FrameResult before = Run(head, 2.0, 3.0, t => rest + new Vec3(50, 0, 0));

        FrameResult frozen = Run(head, 3.0, 4.0, t => rest + new Vec3(-50, 20, -40), freeze: _ => true);

        Assert.True(frozen.Apply);
        Assert.True(frozen.Frozen);
        Assert.True((frozen.Eye - before.Eye).Length < 1e-9, $"moved {(frozen.Eye - before.Eye).Length:F3} mm while frozen");
    }

    [Fact]
    public void Releasing_the_freeze_glides_to_the_head_without_a_jump()
    {
        var settings = new HeadTrackSettings();
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(rest);
        Run(head, 2.0, 3.0, t => rest + new Vec3(50, 0, 0));
        var frames = new List<FrameResult>();
        Run(head, 3.0, 4.0, t => rest + new Vec3(-50, 0, 0), freeze: _ => true, frames: frames);
        int released = frames.Count;
        Assert.True(frames[released - 1].Frozen);
        Assert.Equal(50.0, (frames[released - 1].Eye - frames[released - 1].Window.NeutralEye).X, 0);

        Run(head, 4.0, 5.0, t => rest + new Vec3(-50, 0, 0), frames: frames);

        // 100 mm along a smoothstep over the release time, at 60 fps.
        double maxStep = 100 * 1.5 / settings.FreezeReleaseSeconds / 60 * 1.05;
        for (int i = released; i < frames.Count; i++)
        {
            double step = (frames[i].Eye - frames[i - 1].Eye).Length;
            Assert.True(step <= maxStep, $"step {step:F2} mm in frame {i - released} after the release");
        }
        FrameResult settled = frames[released + (int)Math.Ceiling((settings.FreezeReleaseSeconds + 0.05) * 60)];
        Assert.False(settled.Frozen);
        Assert.Equal(-50.0, (settled.Eye - settled.Window.NeutralEye).X, 0);
    }

    [Fact]
    public void A_freeze_that_begins_mid_release_holds_the_camera_where_it_is()
    {
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(rest);
        Run(head, 2.0, 3.0, t => rest + new Vec3(50, 0, 0));
        Run(head, 3.0, 4.0, t => rest + new Vec3(-50, 0, 0), freeze: _ => true);
        FrameResult gliding = Run(head, 4.0, 4.1, t => rest + new Vec3(-50, 0, 0));

        FrameResult refrozen = Run(head, 4.1, 5.0, t => rest + new Vec3(-50, 0, 0), freeze: _ => true);

        Assert.True((refrozen.Eye - gliding.Eye).Length < 1e-9, $"moved {(refrozen.Eye - gliding.Eye).Length:F3} mm");
    }
}
