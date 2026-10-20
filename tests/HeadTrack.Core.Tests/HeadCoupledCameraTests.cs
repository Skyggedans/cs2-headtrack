using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class HeadCoupledCameraTests
{
    private const double Period = 1.0 / 33.0;

    private static CameraFrame GameCamera() => WindowProjectionTests.Camera(16, 42.6, WindowProjectionTests.GameTangents);

    /// <summary>Feeds the tracker at 33 Hz and runs frames at 60 fps from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static FrameResult Run(HeadCoupledCamera head, double from, double to, Func<double, Vec3?> eye,
        Func<Vec3, Vec3>? clamp = null, Func<double, bool>? gate = null, double depth = 1120)
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
            result = head.Compute(now, camera, depth, gate?.Invoke(now) ?? true, cut: false, clamp);
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
    public void The_window_depth_is_smoothed()
    {
        var rest = new Vec3(0, 0, 600);
        HeadCoupledCamera head = Centred(rest);
        Run(head, 2.0, 3.0, t => rest + new Vec3(50, 0, 0), depth: 1000);

        FrameResult jumped = Run(head, 3.0, 3.02, t => rest + new Vec3(50, 0, 0), depth: 2000);

        Assert.True(jumped.Depth > 1000 && jumped.Depth < 1500, $"depth {jumped.Depth:F0}");
    }
}
