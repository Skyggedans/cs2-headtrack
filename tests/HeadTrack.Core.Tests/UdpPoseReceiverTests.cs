using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class UdpPoseReceiverTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(3);

    private static List<PoseSample> WaitForSamples(PoseRing ring, int count)
    {
        var taken = new List<PoseSample>();
        long last = 0;
        var watch = Stopwatch.StartNew();
        while (taken.Count < count && watch.Elapsed < Patience)
        {
            last = ring.TakeNewer(last, taken);
            Thread.Sleep(5);
        }
        return taken;
    }

    private static void Send(int port, byte[] datagram)
    {
        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.Send(datagram, datagram.Length, new IPEndPoint(IPAddress.Loopback, port));
    }

    [Fact]
    public void A_pose_sent_to_its_port_lands_in_the_ring()
    {
        var ring = new PoseRing(16);
        using var receiver = new UdpPoseReceiver(ring);
        Assert.True(receiver.Start(IPAddress.Loopback, 0), receiver.Error);
        var pose = new OpenTrackPose(1.5, -2.0, 61.0, 3.0, -4.0, 5.0);

        Send(receiver.Port, OpenTrackPacket.Encode(pose));

        List<PoseSample> samples = WaitForSamples(ring, 1);
        Assert.Single(samples);
        Assert.True(samples[0].Pose.BitwiseEquals(pose));
        Assert.Equal(1, receiver.Received);
    }

    [Fact]
    public void Datagrams_that_are_not_poses_are_counted_and_dropped()
    {
        var ring = new PoseRing(16);
        using var receiver = new UdpPoseReceiver(ring);
        Assert.True(receiver.Start(IPAddress.Loopback, 0), receiver.Error);
        var pose = new OpenTrackPose(0, 0, 60, 0, 0, 0);

        Send(receiver.Port, new byte[10]);
        Send(receiver.Port, OpenTrackPacket.Encode(pose));

        List<PoseSample> samples = WaitForSamples(ring, 1);
        Assert.Single(samples);
        Assert.Equal(2, receiver.Received);
        Assert.Equal(1, receiver.Rejected);
    }

    [Fact]
    public void Arrival_times_come_from_the_given_clock()
    {
        var ring = new PoseRing(16);
        using var receiver = new UdpPoseReceiver(ring, () => 123456789L);
        Assert.True(receiver.Start(IPAddress.Loopback, 0), receiver.Error);

        Send(receiver.Port, OpenTrackPacket.Encode(new OpenTrackPose(0, 0, 60, 0, 0, 0)));

        List<PoseSample> samples = WaitForSamples(ring, 1);
        Assert.Equal(123456789L, Assert.Single(samples).Timestamp);
    }

    [Fact]
    public void A_busy_port_is_reported_instead_of_thrown()
    {
        using var occupant = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int busyPort = ((IPEndPoint)occupant.Client.LocalEndPoint!).Port;
        var ring = new PoseRing(16);
        using var receiver = new UdpPoseReceiver(ring);

        bool started = receiver.Start(IPAddress.Loopback, busyPort);

        Assert.False(started);
        Assert.False(receiver.IsRunning);
        Assert.Contains(busyPort.ToString(), receiver.Error);
    }

    [Fact]
    public void Dispose_stops_the_receive_thread_promptly()
    {
        var ring = new PoseRing(16);
        var receiver = new UdpPoseReceiver(ring);
        Assert.True(receiver.Start(IPAddress.Loopback, 0), receiver.Error);
        Assert.True(receiver.IsRunning);
        var watch = Stopwatch.StartNew();

        receiver.Dispose();

        Assert.False(receiver.IsRunning);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"stopping took {watch.Elapsed}");
    }

    [Fact]
    public void The_port_is_free_again_after_dispose()
    {
        var ring = new PoseRing(16);
        int port;
        using (var receiver = new UdpPoseReceiver(ring))
        {
            Assert.True(receiver.Start(IPAddress.Loopback, 0), receiver.Error);
            port = receiver.Port;
        }

        using var again = new UdpPoseReceiver(ring);
        Assert.True(again.Start(IPAddress.Loopback, port), again.Error);
    }
}
