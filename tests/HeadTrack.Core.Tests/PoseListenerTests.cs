using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class PoseListenerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(3);

    /// <summary>A receiver the test can kill or refuse to bind.</summary>
    private sealed class FakeReceiver : IPoseReceiver
    {
        private readonly Func<int, bool> m_Binds;

        public FakeReceiver(Func<int, bool> binds)
        {
            m_Binds = binds;
        }

        public bool IsRunning { get; private set; }

        public int Port { get; private set; }

        public string? Error { get; private set; }

        public string? Fault { get; private set; }

        public bool Start(IPAddress address, int port)
        {
            if (!m_Binds(port))
            {
                Error = $"cannot listen on {address}:{port}: AddressAlreadyInUse";
                return false;
            }
            Port = port;
            IsRunning = true;
            return true;
        }

        public void Die(string why)
        {
            Fault = why;
            IsRunning = false;
        }

        public void Stop()
        {
            IsRunning = false;
        }

        public void Dispose()
        {
            Stop();
        }
    }

    private static int FreePort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private static bool Arrives(PoseRing ring, ref long last, int port, double x)
    {
        using (var client = new UdpClient(AddressFamily.InterNetwork))
        {
            byte[] packet = OpenTrackPacket.Encode(new OpenTrackPose(x, 0, 60, 0, 0, 0));
            client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, port));
        }
        var taken = new List<PoseSample>();
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < Patience)
        {
            last = ring.TakeNewer(last, taken);
            if (taken.Any(s => s.Pose.X == x))
            {
                return true;
            }
            Thread.Sleep(5);
        }
        return false;
    }

    [Fact]
    public void A_new_port_is_listened_on_and_the_old_one_is_freed()
    {
        var ring = new PoseRing(64);
        using var listener = new PoseListener(() => new UdpPoseReceiver(ring));
        int first = FreePort(), second = FreePort();
        long last = 0;

        listener.Update(0.0, IPAddress.Loopback, first);
        Assert.True(Arrives(ring, ref last, first, 1.0), listener.Status);
        listener.Update(0.1, IPAddress.Loopback, second);

        Assert.True(Arrives(ring, ref last, second, 2.0), listener.Status);
        using var reuse = new UdpClient(new IPEndPoint(IPAddress.Loopback, first));
    }

    [Fact]
    public void A_busy_new_port_keeps_the_old_one_listening_and_says_why()
    {
        var ring = new PoseRing(64);
        using var listener = new PoseListener(() => new UdpPoseReceiver(ring));
        int first = FreePort();
        using var occupant = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int busy = ((IPEndPoint)occupant.Client.LocalEndPoint!).Port;
        long last = 0;

        listener.Update(0.0, IPAddress.Loopback, first);
        listener.Update(0.1, IPAddress.Loopback, busy);

        Assert.True(Arrives(ring, ref last, first, 3.0), listener.Status);
        Assert.Contains(busy.ToString(), listener.Status);
    }

    [Fact]
    public void A_receive_thread_that_died_is_restarted_after_pauses_that_double()
    {
        var made = new List<FakeReceiver>();
        using var listener = new PoseListener(() =>
        {
            var receiver = new FakeReceiver(_ => true);
            made.Add(receiver);
            return receiver;
        });
        listener.Update(0.0, IPAddress.Loopback, 4242);
        Assert.Single(made);

        made[^1].Die("ConnectionReset");
        listener.Update(1.0, IPAddress.Loopback, 4242);
        Assert.Contains("ConnectionReset", listener.Status);
        listener.Update(1.0 + PoseListener.FirstRetrySeconds - 0.01, IPAddress.Loopback, 4242);
        Assert.Single(made);
        listener.Update(1.0 + PoseListener.FirstRetrySeconds, IPAddress.Loopback, 4242);
        Assert.Equal(2, made.Count);

        made[^1].Die("ConnectionReset");
        listener.Update(3.0, IPAddress.Loopback, 4242);
        listener.Update(3.0 + 2 * PoseListener.FirstRetrySeconds - 0.01, IPAddress.Loopback, 4242);
        Assert.Equal(2, made.Count);
        listener.Update(3.0 + 2 * PoseListener.FirstRetrySeconds, IPAddress.Loopback, 4242);
        Assert.Equal(3, made.Count);
        Assert.True(made[^1].IsRunning);
    }

    [Fact]
    public void A_port_that_stays_busy_is_retried_without_a_bind_every_frame()
    {
        int attempts = 0;
        using var listener = new PoseListener(() => new FakeReceiver(_ =>
        {
            attempts++;
            return false;
        }));

        for (int i = 0; i < 600; i++)
        {
            listener.Update(i / 60.0, IPAddress.Loopback, 4242);
        }

        Assert.InRange(attempts, 2, 6);
        Assert.Contains("4242", listener.Status);
    }

    [Fact]
    public void Its_state_says_whether_it_listens_where_it_was_asked()
    {
        var busy = new HashSet<int> { 5000 };
        var made = new List<FakeReceiver>();
        var listener = new PoseListener(() =>
        {
            var receiver = new FakeReceiver(port => !busy.Contains(port));
            made.Add(receiver);
            return receiver;
        });
        Assert.Equal(ListenerState.Idle, listener.State);

        listener.Update(0.0, IPAddress.Loopback, 4242);
        Assert.Equal(ListenerState.Listening, listener.State);
        Assert.Equal(4242, listener.ListeningPort);

        listener.Update(0.1, IPAddress.Loopback, 5000);
        Assert.Equal(ListenerState.KeptOld, listener.State);
        Assert.Equal(4242, listener.ListeningPort);

        busy.Remove(5000);
        listener.Update(0.1 + PoseListener.FirstRetrySeconds, IPAddress.Loopback, 5000);
        Assert.Equal(ListenerState.Listening, listener.State);
        Assert.Equal(5000, listener.ListeningPort);

        made[^1].Die("ConnectionReset");
        listener.Update(2.0, IPAddress.Loopback, 5000);
        Assert.Equal(ListenerState.Down, listener.State);
        Assert.Equal(0, listener.ListeningPort);

        listener.Dispose();
        Assert.Equal(ListenerState.Idle, listener.State);
    }

    [Fact]
    public void Only_a_port_that_could_not_be_had_or_a_dead_receiver_is_a_failure()
    {
        // The options page registers before the first Update: an idle
        // listener must not warn, or every start shows a warning.
        var busy = new HashSet<int> { 5000 };
        var made = new List<FakeReceiver>();
        var listener = new PoseListener(() =>
        {
            var receiver = new FakeReceiver(port => !busy.Contains(port));
            made.Add(receiver);
            return receiver;
        });
        Assert.False(listener.Failing);

        listener.Update(0.0, IPAddress.Loopback, 4242);
        Assert.False(listener.Failing);
        listener.Update(0.1, IPAddress.Loopback, 5000);
        Assert.True(listener.Failing);
        busy.Remove(5000);
        listener.Update(0.1 + PoseListener.FirstRetrySeconds, IPAddress.Loopback, 5000);
        Assert.False(listener.Failing);
        made[^1].Die("ConnectionReset");
        Assert.True(listener.Failing);
    }

    [Fact]
    public void A_first_port_that_cannot_be_had_leaves_it_down()
    {
        using var listener = new PoseListener(() => new FakeReceiver(_ => false));

        listener.Update(0.0, IPAddress.Loopback, 4242);

        Assert.Equal(ListenerState.Down, listener.State);
        Assert.Equal(0, listener.ListeningPort);
    }

    [Fact]
    public void Stopping_a_receiver_is_not_a_fault()
    {
        var ring = new PoseRing(4);
        var receiver = new UdpPoseReceiver(ring);
        Assert.True(receiver.Start(IPAddress.Loopback, 0), receiver.Error);
        using (var client = new UdpClient(AddressFamily.InterNetwork))
        {
            byte[] packet = OpenTrackPacket.Encode(new OpenTrackPose(1, 0, 60, 0, 0, 0));
            client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, receiver.Port));
        }
        var taken = new List<PoseSample>();
        var watch = Stopwatch.StartNew();
        while (taken.Count == 0 && watch.Elapsed < TimeSpan.FromSeconds(3))
        {
            ring.TakeNewer(0, taken);
            Thread.Sleep(5);
        }
        Assert.NotEmpty(taken);
        // The thread is back in its blocking receive, which the stop interrupts.
        Thread.Sleep(50);

        receiver.Dispose();

        Assert.Null(receiver.Fault);
    }
}
