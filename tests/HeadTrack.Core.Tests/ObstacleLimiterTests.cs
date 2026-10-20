using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class ObstacleLimiterTests
{
    private static readonly Vec3 Base = new(10, 20, 30);

    private static void Near(Vec3 expected, Vec3 actual, double tolerance = 1e-9)
    {
        Assert.True((expected - actual).Length <= tolerance, $"expected {expected}, got {actual}");
    }

    [Fact]
    public void A_cast_runs_from_the_game_camera_along_the_move_and_past_it_by_the_clearance()
    {
        Assert.True(ObstacleCast.TryCreate(Base, Base + new Vec3(3, 4, 0), 2.0, out ObstacleCast cast));

        Near(Base, cast.Start);
        Near(Base + new Vec3(4.2, 5.6, 0), cast.End);
        Assert.Equal(7.0, cast.Length, 12);
    }

    [Fact]
    public void A_move_too_small_to_matter_casts_nothing()
    {
        Assert.False(ObstacleCast.TryCreate(Base, Base + new Vec3(1e-7, 0, 0), 2.0, out _));
    }

    [Fact]
    public void A_hit_leaves_the_distance_to_it_less_the_clearance()
    {
        ObstacleCast.TryCreate(Base, Base + new Vec3(3, 4, 0), 2.0, out ObstacleCast cast);

        Assert.Equal(1.5, cast.ClearLength(0.5), 12);
    }

    [Fact]
    public void Without_any_cast_result_the_move_is_not_limited()
    {
        var limiter = new ObstacleLimiter();
        Vec3 wanted = Base + new Vec3(300, 400, 0);

        Near(wanted, limiter.Limit(Base, wanted));
    }

    [Fact]
    public void A_wall_in_the_way_stops_the_camera_short_of_it()
    {
        var limiter = new ObstacleLimiter();

        limiter.Update(1.0 / 60, 1.5, releaseSpeed: 5);

        Near(Base + new Vec3(0.9, 1.2, 0), limiter.Limit(Base, Base + new Vec3(3, 4, 0)));
    }

    [Fact]
    public void Inside_the_allowance_the_camera_follows_the_head_exactly()
    {
        var limiter = new ObstacleLimiter();
        limiter.Update(1.0 / 60, 3.0, releaseSpeed: 5);
        Vec3 wanted = Base + new Vec3(1.2, 1.6, 0);

        Near(wanted, limiter.Limit(Base, wanted));
    }

    [Fact]
    public void A_closer_wall_applies_at_once_and_a_farther_one_only_at_the_release_speed()
    {
        var limiter = new ObstacleLimiter();
        limiter.Update(0.1, 3.0, releaseSpeed: 5);
        Assert.Equal(3.0, limiter.Allowed, 12);

        limiter.Update(0.1, 1.0, releaseSpeed: 5);
        Assert.Equal(1.0, limiter.Allowed, 12);

        limiter.Update(0.1, 10.0, releaseSpeed: 5);
        Assert.Equal(1.5, limiter.Allowed, 12);

        limiter.Update(0.1, double.PositiveInfinity, releaseSpeed: 5);
        Assert.Equal(2.0, limiter.Allowed, 12);

        limiter.Update(0.1, null, releaseSpeed: 5);
        Assert.Equal(2.5, limiter.Allowed, 12);
    }

    [Fact]
    public void A_wall_closer_than_the_clearance_keeps_the_camera_where_the_game_put_it()
    {
        var limiter = new ObstacleLimiter();

        limiter.Update(1.0 / 60, -0.5, releaseSpeed: 5);

        Near(Base, limiter.Limit(Base, Base + new Vec3(3, 4, 0)));
    }

    [Fact]
    public void Reset_lifts_the_limit()
    {
        var limiter = new ObstacleLimiter();
        limiter.Update(1.0 / 60, 0.5, releaseSpeed: 5);

        limiter.Reset();

        Vec3 wanted = Base + new Vec3(3, 4, 0);
        Near(wanted, limiter.Limit(Base, wanted));
    }
}
