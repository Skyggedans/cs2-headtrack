using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class OneEuroFilterTests
{
    private const double Dt = 1.0 / 33.0;

    [Fact]
    public void The_first_sample_passes_through_unchanged()
    {
        var filter = new OneEuroFilter(minCutoff: 0.5, beta: 0.02, dCutoff: 2.0);

        Assert.Equal(12.5, filter.Filter(12.5, Dt));
        Assert.True(filter.HasValue);
    }

    [Fact]
    public void A_constant_input_stays_constant()
    {
        var filter = new OneEuroFilter(0.5, 0.02, 2.0);
        double y = 0;
        for (int i = 0; i < 100; i++)
        {
            y = filter.Filter(40.0, Dt);
        }

        Assert.Equal(40.0, y, 12);
    }

    [Fact]
    public void Noise_at_rest_is_attenuated()
    {
        var rng = new Random(1);
        var filter = new OneEuroFilter(0.5, 0.02, 2.0);
        double sumIn = 0, sumOut = 0;
        for (int i = 0; i < 3000; i++)
        {
            double noise = (rng.NextDouble() - 0.5) * 2.0;
            double y = filter.Filter(noise, Dt);
            if (i > 100)
            {
                sumIn += noise * noise;
                sumOut += y * y;
            }
        }

        Assert.True(Math.Sqrt(sumOut) < 0.35 * Math.Sqrt(sumIn), $"rms ratio {Math.Sqrt(sumOut / sumIn):F3}");
    }

    [Fact]
    public void Speed_raises_the_cutoff_so_a_fast_move_lags_less_than_a_fixed_low_pass()
    {
        var adaptive = new OneEuroFilter(0.5, 0.02, 2.0);
        var fixedLowPass = new OneEuroFilter(0.5, 0.0, 2.0);
        double x = 0, adaptiveLag = 0, fixedLag = 0;
        for (int i = 0; i < 33; i++)
        {
            x += 200.0 * Dt; // 200 mm/s
            adaptiveLag = x - adaptive.Filter(x, Dt);
            fixedLag = x - fixedLowPass.Filter(x, Dt);
        }

        Assert.True(adaptiveLag < 0.25 * fixedLag, $"adaptive {adaptiveLag:F1} mm vs fixed {fixedLag:F1} mm");
        Assert.True(adaptiveLag < 15.0, $"lag at 200 mm/s: {adaptiveLag:F1} mm");
    }

    [Fact]
    public void A_zero_or_negative_time_step_does_not_produce_nan()
    {
        var filter = new OneEuroFilter(0.5, 0.02, 2.0);
        filter.Filter(1.0, Dt);

        double same = filter.Filter(2.0, 0.0);
        double back = filter.Filter(3.0, -0.01);

        Assert.False(double.IsNaN(same));
        Assert.False(double.IsNaN(back));
    }

    [Fact]
    public void Reset_forgets_the_state()
    {
        var filter = new OneEuroFilter(0.5, 0.02, 2.0);
        filter.Filter(1.0, Dt);
        filter.Filter(5.0, Dt);

        filter.Reset();

        Assert.False(filter.HasValue);
        Assert.Equal(100.0, filter.Filter(100.0, Dt));
    }
}
