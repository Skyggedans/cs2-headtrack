using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class EyeFilterTests
{
    private const double Dt = 1.0 / 33.0;

    private static EyeFilter Default() => new(
        new OneEuroParameters(0.5, 0.02, 2.0),
        new OneEuroParameters(0.3, 0.01, 1.5));

    [Fact]
    public void The_first_sample_passes_through()
    {
        var filter = Default();

        Vec3 y = filter.Filter(new Vec3(10, -20, 600), Dt);

        Assert.Equal(10, y.X);
        Assert.Equal(-20, y.Y);
        Assert.Equal(600, y.Z);
    }

    [Fact]
    public void A_move_mostly_along_x_stays_on_its_line_because_x_and_y_share_one_speed()
    {
        var filter = Default();
        var direction = new Vec3(1.0, 0.2, 0).Normalized();
        double worstCrossTrack = 0;
        for (int i = 0; i <= 33; i++)
        {
            Vec3 x = direction * (200.0 * i * Dt) + new Vec3(0, 0, 600);
            Vec3 y = filter.Filter(x, Dt);
            Vec3 lateral = new Vec3(y.X, y.Y, 0);
            double along = Vec3.Dot(lateral, direction);
            double crossTrack = (lateral - direction * along).Length;
            worstCrossTrack = Math.Max(worstCrossTrack, crossTrack);
        }

        Assert.True(worstCrossTrack < 1e-9, $"cross-track error {worstCrossTrack} mm");
    }

    [Fact]
    public void Depth_is_filtered_with_its_own_slower_parameters()
    {
        var filter = Default();
        Vec3 y = default;
        double moved = 0;
        for (int i = 0; i < 20; i++)
        {
            moved += 100.0 * Dt;
            y = filter.Filter(new Vec3(moved, 0, 600 + moved), Dt);
        }

        double lagX = moved - y.X;
        double lagZ = 600 + moved - y.Z;
        Assert.True(lagZ > lagX, $"lag x {lagX:F2} mm, z {lagZ:F2} mm");
    }

    [Fact]
    public void Reset_starts_again_from_the_next_sample()
    {
        var filter = Default();
        filter.Filter(new Vec3(0, 0, 600), Dt);
        filter.Filter(new Vec3(50, 0, 600), Dt);

        filter.Reset();
        Vec3 y = filter.Filter(new Vec3(-30, 5, 500), Dt);

        Assert.Equal(-30, y.X);
        Assert.Equal(5, y.Y);
        Assert.Equal(500, y.Z);
    }
}
