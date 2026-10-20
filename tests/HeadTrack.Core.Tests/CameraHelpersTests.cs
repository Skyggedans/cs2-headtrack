using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class CoverageFrustumTests
{
    private static readonly FrustumTangents Game = WindowProjectionTests.GameTangents;

    private static double GameFovFactor => 1.0 / Game.Top;

    private static double GameAspect => Game.Right / Game.Top;

    [Fact]
    public void At_zero_offset_the_games_own_arguments_come_back_unchanged()
    {
        PlanePacketArguments cover = CoverageFrustum.Cover(GameFovFactor, GameAspect, Game, Game);

        Assert.Equal(GameFovFactor, cover.FovFactor, 12);
        Assert.Equal(GameAspect, cover.Aspect, 12);
    }

    [Fact]
    public void The_covering_pyramid_contains_every_ray_of_the_off_axis_frustum()
    {
        var rng = new Random(8);
        for (int i = 0; i < 500; i++)
        {
            double shiftX = (rng.NextDouble() - 0.5) * 2.4, shiftY = (rng.NextDouble() - 0.5) * 1.8, scale = 0.6 + rng.NextDouble();
            var view = new FrustumTangents(
                (Game.Left + shiftX) * scale, (Game.Right + shiftX) * scale,
                (Game.Bottom + shiftY) * scale, (Game.Top + shiftY) * scale);

            PlanePacketArguments cover = CoverageFrustum.Cover(GameFovFactor, GameAspect, Game, view);

            double tanV = 1.0 / cover.FovFactor, tanH = cover.Aspect * tanV;
            for (int k = 0; k < 20; k++)
            {
                double x = view.Left + rng.NextDouble() * (view.Right - view.Left);
                double y = view.Bottom + rng.NextDouble() * (view.Top - view.Bottom);
                Assert.True(Math.Abs(x) <= tanH && Math.Abs(y) <= tanV, $"ray ({x:F3}, {y:F3}) outside ({tanH:F3}, {tanV:F3})");
            }
            foreach (double x in new[] { view.Left, view.Right })
            {
                foreach (double y in new[] { view.Bottom, view.Top })
                {
                    Assert.True(Math.Abs(x) <= tanH && Math.Abs(y) <= tanV, "a corner ray is outside");
                }
            }
        }
    }

    [Fact]
    public void A_game_argument_narrower_than_the_rendered_frustum_is_widened_to_cover_it()
    {
        double narrowFovFactor = 1.0 / (0.8 * Game.Top);
        var view = new FrustumTangents(Game.Left + 0.1, Game.Right + 0.1, Game.Bottom, Game.Top);

        PlanePacketArguments cover = CoverageFrustum.Cover(narrowFovFactor, GameAspect, Game, view);

        Assert.True(1.0 / cover.FovFactor >= Game.Top);
        Assert.True(cover.Aspect / cover.FovFactor >= view.Right);
    }
}

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
    public void Looking_straight_down_the_zoom_is_used()
    {
        var forward = new Vec3(0, -1, 0.0001).Normalized();

        double depth = WindowDepth.AimPoint(new Vec3(0, 0, 0), new Vec3(0, 800, 0), forward, zoom: 750, near: 1, far: 90000, zoomMin: 10);

        Assert.Equal(750, depth, 9);
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
        ramp.Update(0.0, on: true);
        double mid = ramp.Update(0.2, on: true);
        double full = ramp.Update(0.45, on: true);
        double falling = ramp.Update(0.65, on: false);
        double off = ramp.Update(0.9, on: false);

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

        double cut = ramp.Cut();

        Assert.Equal(0.0, cut);
        Assert.Equal(0.0, ramp.Weight);
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

public class ExponentialSmootherTests
{
    [Fact]
    public void It_reaches_63_percent_of_a_step_after_one_time_constant()
    {
        var smoother = new ExponentialSmoother(0.3);
        smoother.Reset(0);
        double value = 0;
        for (int i = 0; i < 30; i++)
        {
            value = smoother.Update(100, 0.01);
        }

        Assert.Equal(100 * (1 - Math.Exp(-1)), value, 6);
    }

    [Fact]
    public void The_first_value_is_taken_as_is()
    {
        var smoother = new ExponentialSmoother(0.3);

        Assert.Equal(42.0, smoother.Update(42, 0.016));
    }
}
