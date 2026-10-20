using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class PhysicalLensTests
{
    [Fact]
    public void Its_gate_fit_none_frustum_is_the_requested_one()
    {
        var view = new FrustumTangents(-0.41, 0.79, -0.22, 0.45);

        PhysicalLens lens = PhysicalLens.FromTangents(view, 24.0);
        FrustumTangents back = lens.Tangents;

        Assert.Equal(24.0, lens.SensorHeight);
        Assert.Equal(view.Left, back.Left, 12);
        Assert.Equal(view.Right, back.Right, 12);
        Assert.Equal(view.Bottom, back.Bottom, 12);
        Assert.Equal(view.Top, back.Top, 12);
    }

    [Fact]
    public void A_symmetric_frustum_needs_no_lens_shift()
    {
        FrustumTangents game = WindowProjectionTests.GameTangents;

        PhysicalLens lens = PhysicalLens.FromTangents(game, 24.0);

        Assert.Equal(0.0, lens.ShiftX, 12);
        Assert.Equal(0.0, lens.ShiftY, 12);
        Assert.Equal(game.Right / game.Top, lens.SensorWidth / lens.SensorHeight, 12);
        Assert.Equal(2 * Math.Atan(game.Top) * 180 / Math.PI, lens.VerticalFieldOfViewDegrees, 9);
    }
}

public class WindowDepthTests
{
    [Fact]
    public void Looking_down_at_the_pivot_the_depth_is_the_distance_along_the_view()
    {
        var forward = new Vec3(0, -Math.Sqrt(0.5), Math.Sqrt(0.5));
        var position = new Vec3(0, 1000, -1000);
        var pivot = new Vec3(0, 0, 0);

        double depth = WindowDepth.AimPoint(pivot, position, forward, zoom: 1400, near: 1, far: 90000, zoomMin: 10);

        Assert.Equal(Math.Sqrt(2) * 1000, depth, 6);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(-3.0)]
    [InlineData(-10.0)]
    public void Looking_up_a_lifted_camera_keeps_the_horizontal_depth(double pitchDeg)
    {
        // Looking up the pivot is off screen below the view; the lift must
        // not shorten the depth there, as the pivot's own depth would.
        double pitch = pitchDeg * Math.PI / 180;
        var forward = new Vec3(0, -Math.Sin(pitch), Math.Cos(pitch));
        var pivot = new Vec3(0, 0, 0);

        double depth = WindowDepth.AimPoint(pivot, pivot - forward * 200 + new Vec3(0, 17, 0), forward, zoom: 200, near: 1, far: 90000, zoomMin: 10);

        Assert.Equal(200, depth, 6);
    }

    [Fact]
    public void Looking_up_the_depth_stays_positive_and_follows_the_horizontal_distance()
    {
        double pitch = -20 * Math.PI / 180;
        var forward = new Vec3(0, -Math.Sin(pitch), Math.Cos(pitch));
        var position = new Vec3(0, 30, -200);
        var pivot = new Vec3(0, 0, 0);

        double depth = WindowDepth.AimPoint(pivot, position, forward, zoom: 200, near: 1, far: 90000, zoomMin: 10);

        Assert.Equal(200 / Math.Cos(pitch), depth, 6);
    }

    [Fact]
    public void Looking_straight_down_the_depth_is_the_pivots_own()
    {
        // The game keeps the camera 50 m above its orbit here (lift and floor).
        var forward = new Vec3(0, -1, 0.0001).Normalized();

        double depth = WindowDepth.AimPoint(new Vec3(0, 0, 0), new Vec3(0, 800, 0), forward, zoom: 750, near: 1, far: 90000, zoomMin: 10);

        Assert.Equal(800, depth, 3);
    }

    [Theory]
    [InlineData(0.3, 200.0, 17.0)]
    [InlineData(15.0, 200.0, 17.0)]
    [InlineData(25.0, 120.0, 9.0)]
    [InlineData(45.0, 200.0, 17.0)]
    [InlineData(60.0, 200.0, 17.0)]
    [InlineData(85.0, 200.0, 17.0)]
    public void A_camera_lifted_above_its_orbit_keeps_the_pivot_still_on_screen(double pitch, double zoom, double lift)
    {
        // CameraController puts the camera half the smallest zoom above its
        // orbit, and its soft floor lifts it further.
        CameraFrame lifted = WindowProjectionTests.Camera(30, pitch, WindowProjectionTests.GameTangents);
        var pivot = new Vec3(120, 40, -310);
        var camera = new CameraFrame(pivot - lifted.Forward * zoom + new Vec3(0, lift, 0), lifted.Right, lifted.Up, lifted.Forward,
            lifted.Near, lifted.Far, lifted.Tangents);

        double depth = WindowDepth.AimPoint(pivot, camera.Position, camera.Forward, zoom: zoom, near: 1, far: 90000, zoomMin: 10);
        WindowGeometry window = WindowProjection.Window(camera, depth, 597, 336);
        (double X, double Y) Seen(Vec3 eye)
        {
            OffAxisView view = WindowProjection.ViewFrom(camera, window, WindowProjection.EyeToWorld(camera, window, eye));
            return WindowProjectionTests.Ndc(view.Matrix, view.Position, camera, pivot);
        }

        (double x0, double y0) = Seen(window.NeutralEye);
        (double x1, double y1) = Seen(window.NeutralEye + new Vec3(100, -60, -80));
        Assert.Equal(x0, x1, 9);
        Assert.Equal(y0, y1, 9);
    }

    [Fact]
    public void A_push_along_the_view_keeps_the_depth_continuous_down_to_straight_down()
    {
        // CameraCollisionSystem moves the camera along its view line, here 50 m back.
        var pivot = new Vec3(0, 0, 0);
        double Depth(double pitchDeg)
        {
            double pitch = pitchDeg * Math.PI / 180;
            var forward = new Vec3(0, -Math.Sin(pitch), Math.Cos(pitch));
            return WindowDepth.AimPoint(pivot, pivot - forward * 150, forward, zoom: 100, near: 1, far: 90000, zoomMin: 10);
        }

        Assert.Equal(Depth(84.25), Depth(84.27), 6);
        Assert.Equal(150, Depth(84.25), 6);
        Assert.Equal(150, Depth(90), 6);
    }

    [Fact]
    public void A_sideways_slide_of_the_camera_leaves_the_depth_alone()
    {
        // Pitch 84 degrees, zoom 300 m, the camera pushed 15 m sideways by collisions.
        double pitch = 84 * Math.PI / 180;
        var forward = new Vec3(0, -Math.Sin(pitch), Math.Cos(pitch));
        var pivot = new Vec3(0, 0, 0);
        Vec3 slid = pivot - forward * 300 + new Vec3(15, 0, 0);

        double depth = WindowDepth.AimPoint(pivot, slid, forward, zoom: 300, near: 1, far: 90000, zoomMin: 10);

        Assert.Equal(300, depth, 6);
    }

    [Fact]
    public void Looking_up_towards_straight_up_the_depth_has_no_step()
    {
        // The game tilts this far up below 217 m of zoom, or at any zoom with
        // its legacy camera; CameraCollisionSystem has pushed it 50 m back
        // along the view line. Straight up there is no horizontal direction,
        // so the depth must reach the zoom without a jump on the way.
        var pivot = new Vec3(0, 0, 0);
        double Depth(double horizontal)
        {
            var forward = new Vec3(0, Math.Sqrt(1 - horizontal * horizontal), horizontal);
            return WindowDepth.AimPoint(pivot, pivot - forward * 350, forward, zoom: 300, near: 1, far: 90000, zoomMin: 10);
        }

        double largestStep = 0;
        for (double h = 0; h < 0.2; h += 1e-4)
        {
            largestStep = Math.Max(largestStep, Math.Abs(Depth(h + 1e-4) - Depth(h)));
        }
        Assert.True(largestStep < 0.5, $"the depth stepped {largestStep:F2} m");
        Assert.Equal(350, Depth(0.2), 6);
        Assert.Equal(300, Depth(0), 6);
    }

    [Fact]
    public void The_depth_is_continuous_where_the_view_turns_from_down_to_up()
    {
        // The game's lift: 5 m above the orbit.
        var pivot = new Vec3(0, 0, 0);
        double Depth(double pitchDeg)
        {
            double pitch = pitchDeg * Math.PI / 180;
            var forward = new Vec3(0, -Math.Sin(pitch), Math.Cos(pitch));
            return WindowDepth.AimPoint(pivot, pivot - forward * 300 + new Vec3(0, 5, 0), forward, zoom: 300, near: 1, far: 90000, zoomMin: 10);
        }

        Assert.Equal(Depth(1e-6), Depth(-1e-6), 3);
    }

    [Fact]
    public void A_nan_pivot_gives_the_nearest_sane_depth()
    {
        var forward = new Vec3(0, -Math.Sqrt(0.5), Math.Sqrt(0.5));

        double depth = WindowDepth.AimPoint(new Vec3(double.NaN, 0, 0), new Vec3(0, 1000, -1000), forward, zoom: 1400, near: 1, far: 90000, zoomMin: 10);

        Assert.Equal(10, depth, 9);
    }

    [Fact]
    public void The_depth_is_clamped_to_a_sane_range()
    {
        var forward = new Vec3(0, -Math.Sqrt(0.5), Math.Sqrt(0.5));

        double tooNear = WindowDepth.AimPoint(new Vec3(0, 0, 0.5), new Vec3(0, 0.5, 0), forward, zoom: 1, near: 1, far: 1000, zoomMin: 10);
        double tooFar = WindowDepth.AimPoint(new Vec3(0, 0, 1e6), new Vec3(0, 1e6, 0), forward, zoom: 1e6, near: 1, far: 1000, zoomMin: 10);

        Assert.Equal(10, tooNear, 9);
        Assert.Equal(500, tooFar, 9);
    }
}

public class EffectRampTests
{
    [Fact]
    public void It_rises_and_falls_smoothly_over_the_ramp_time()
    {
        var ramp = new EffectRamp(0.4);
        double RunTo(double from, double to, bool on)
        {
            double weight = 0;
            for (int i = 0; from + i * 0.05 <= to + 1e-9; i++)
            {
                weight = ramp.Update(from + i * 0.05, on);
            }
            return weight;
        }

        double mid = RunTo(0.0, 0.2, on: true);
        double full = RunTo(0.25, 0.45, on: true);
        double falling = RunTo(0.5, 0.65, on: false);
        double off = RunTo(0.7, 0.9, on: false);

        Assert.Equal(0.5, mid, 9);
        Assert.Equal(1.0, full, 9);
        Assert.Equal(0.5, falling, 9);
        Assert.Equal(0.0, off, 9);
    }

    [Fact]
    public void A_cut_drops_to_zero_at_once()
    {
        var ramp = new EffectRamp(0.4);
        ramp.Update(0.0, on: true);
        ramp.Update(1.0, on: true);

        double cut = ramp.Cut(1.0);

        Assert.Equal(0.0, cut);
        Assert.Equal(0.0, ramp.Weight);
    }

    [Fact]
    public void After_a_cut_it_ramps_in_from_zero_however_long_ago_it_last_ran()
    {
        var ramp = new EffectRamp(0.4);
        ramp.Update(0.0, on: true);
        ramp.Update(1.0, on: true);

        ramp.Cut(31.0);
        double first = ramp.Update(31.0 + 1.0 / 60, on: true);

        Assert.True(first < 0.05, $"weight {first:F3} one frame after the cut");
    }

    [Fact]
    public void A_long_pause_between_updates_counts_as_one_short_frame()
    {
        var ramp = new EffectRamp(0.4);
        ramp.Update(0.0, on: false);

        double after = ramp.Update(30.0, on: true);

        Assert.True(after < 0.5, $"weight {after:F3} right after a 30 s pause");
    }

    [Fact]
    public void A_reversal_mid_ramp_continues_from_the_current_weight()
    {
        var ramp = new EffectRamp(0.4);
        ramp.Update(0.0, on: true);
        double up = ramp.Update(0.1, on: true);
        double down = ramp.Update(0.11, on: false);

        Assert.True(Math.Abs(down - up) < 0.05, $"jumped from {up:F3} to {down:F3}");
    }
}

public class GroundFloorTests
{
    [Fact]
    public void The_gameplay_floor_is_the_games_own_lift()
    {
        // CameraController: half the smallest zoom, plus a tenth of the zoom beyond it.
        Assert.Equal(5.0, GroundFloor.Gameplay(zoom: 10, zoomMin: 10), 12);
        Assert.Equal(104.0, GroundFloor.Gameplay(zoom: 1000, zoomMin: 10), 12);
    }

    [Fact]
    public void Where_the_vanilla_camera_clears_it_the_floor_is_what_the_game_keeps()
    {
        Assert.Equal(GroundFloor.OrbitPadding, GroundFloor.Offset(GroundFloor.OrbitPadding, vanillaClearance: 3.0), 12);
        Assert.Equal(5.0, GroundFloor.Offset(GroundFloor.Gameplay(zoom: 10, zoomMin: 10), vanillaClearance: 5.2), 12);
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(-1.5)]
    public void The_floor_never_lifts_the_camera_above_the_vanilla_one(double vanillaClearance)
    {
        // The surface sampled under the vanilla camera can lie above it: the
        // water system's coarse max-height grid on a steep slope, or a road on
        // a retaining wall under a follow camera.
        double offset = GroundFloor.Offset(GroundFloor.OrbitPadding, vanillaClearance);

        Assert.True(offset <= vanillaClearance, $"floor {offset} m above the surface, the vanilla camera {vanillaClearance} m");
    }
}
