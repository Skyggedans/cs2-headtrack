using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class PoseRingTests
{
    private static OpenTrackPose Pose(double x) => new(x, 0, 60, 0, 0, 0);

    [Fact]
    public void Samples_come_out_in_arrival_order_with_their_timestamps()
    {
        var ring = new PoseRing(16);
        ring.Push(100, Pose(1));
        ring.Push(200, Pose(2));
        ring.Push(300, Pose(3));
        var taken = new List<PoseSample>();

        long last = ring.TakeNewer(0, taken);

        Assert.Equal(3, last);
        Assert.Equal(new[] { 100L, 200L, 300L }, taken.Select(s => s.Timestamp));
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, taken.Select(s => s.Pose.X));
    }

    [Fact]
    public void Only_samples_newer_than_the_given_sequence_are_taken()
    {
        var ring = new PoseRing(16);
        ring.Push(100, Pose(1));
        var taken = new List<PoseSample>();
        long last = ring.TakeNewer(0, taken);
        taken.Clear();
        ring.Push(200, Pose(2));

        last = ring.TakeNewer(last, taken);

        Assert.Equal(2, last);
        Assert.Equal(new[] { 2.0 }, taken.Select(s => s.Pose.X));
    }

    [Fact]
    public void A_bitwise_repeat_of_the_previous_pose_is_dropped_and_counted()
    {
        var ring = new PoseRing(16);
        ring.Push(100, Pose(1));
        ring.Push(104, Pose(1));
        ring.Push(108, Pose(1));
        ring.Push(130, Pose(2));
        var taken = new List<PoseSample>();

        ring.TakeNewer(0, taken);

        Assert.Equal(new[] { 100L, 130L }, taken.Select(s => s.Timestamp));
        Assert.Equal(2, ring.Duplicates);
    }

    [Fact]
    public void A_pose_equal_to_an_older_one_but_not_the_last_is_kept()
    {
        var ring = new PoseRing(16);
        ring.Push(100, Pose(1));
        ring.Push(130, Pose(2));
        ring.Push(160, Pose(1));
        var taken = new List<PoseSample>();

        ring.TakeNewer(0, taken);

        Assert.Equal(3, taken.Count);
    }

    [Fact]
    public void Overflow_keeps_the_newest_samples_and_counts_the_lost_ones()
    {
        var ring = new PoseRing(4);
        for (int i = 1; i <= 10; i++)
        {
            ring.Push(i * 10, Pose(i));
        }
        var taken = new List<PoseSample>();

        long last = ring.TakeNewer(0, taken);

        Assert.Equal(10, last);
        Assert.Equal(new[] { 7.0, 8.0, 9.0, 10.0 }, taken.Select(s => s.Pose.X));
        Assert.Equal(6, ring.Overwritten);
    }

    [Fact]
    public void A_reader_that_fell_behind_gets_what_is_left_and_learns_how_much_it_missed()
    {
        var ring = new PoseRing(4);
        var taken = new List<PoseSample>();
        ring.Push(10, Pose(1));
        long last = ring.TakeNewer(0, taken);
        taken.Clear();
        for (int i = 2; i <= 9; i++)
        {
            ring.Push(i * 10, Pose(i));
        }

        last = ring.TakeNewer(last, taken);

        Assert.Equal(9, last);
        Assert.Equal(new[] { 6.0, 7.0, 8.0, 9.0 }, taken.Select(s => s.Pose.X));
    }

    [Fact]
    public void Concurrent_pushes_and_takes_lose_nothing_that_fits()
    {
        var ring = new PoseRing(1024);
        const int count = 20000;
        var taken = new List<PoseSample>();
        var producer = new Thread(() =>
        {
            for (int i = 1; i <= count; i++)
            {
                ring.Push(i, Pose(i));
            }
        });
        producer.Start();
        long last = 0;
        while (producer.IsAlive || last < count)
        {
            last = ring.TakeNewer(last, taken);
            if (ring.Overwritten > 0)
            {
                break;
            }
        }
        producer.Join();
        last = ring.TakeNewer(last, taken);

        // Either nothing was overwritten and every sample arrived in order, or
        // the reader lagged by more than the capacity, which is counted.
        if (ring.Overwritten == 0)
        {
            Assert.Equal(count, taken.Count);
            Assert.Equal(Enumerable.Range(1, count).Select(i => (double)i), taken.Select(s => s.Pose.X));
        }
        else
        {
            Assert.True(taken.Select(s => s.Timestamp).Zip(taken.Skip(1).Select(s => s.Timestamp)).All(p => p.First < p.Second));
        }
    }
}
