using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace HeadTrack.Core
{
    /// <summary>
    /// Receives OpenTrack pose datagrams on a background thread and pushes
    /// each valid one, stamped with its arrival time, into a <see cref="PoseRing"/>.
    /// The thread never logs or touches the game; the main thread reads the
    /// counters and <see cref="Error"/>. Under Wine the socket is a host socket,
    /// so a Linux sender such as tobii-opentrack reaches it directly.
    /// </summary>
    public sealed class UdpPoseReceiver : IPoseReceiver
    {
        /// <summary>How long a blocked receive waits before re-checking the stop flag.</summary>
        private const int ReceiveTimeoutMs = 250;

        /// <summary>How long Dispose waits for the thread to end.</summary>
        private const int JoinTimeoutMs = 500;

        private readonly PoseRing m_Ring;
        private readonly Func<long> m_Clock;
        private Socket? m_Socket;
        private Thread? m_Thread;
        private volatile bool m_Stop;
        private volatile string? m_Fault;
        private long m_Received;
        private long m_Rejected;

        /// <param name="ring">Where valid poses go.</param>
        /// <param name="clock">Arrival timestamps; Stopwatch ticks by default.</param>
        public UdpPoseReceiver(PoseRing ring, Func<long>? clock = null)
        {
            m_Ring = ring ?? throw new ArgumentNullException(nameof(ring));
            m_Clock = clock ?? Stopwatch.GetTimestamp;
        }

        /// <summary>The bound port; useful after starting on port 0.</summary>
        public int Port { get; private set; }

        /// <summary>Why the last <see cref="Start"/> failed, or null.</summary>
        public string? Error { get; private set; }

        public bool IsRunning => m_Thread != null && m_Thread.IsAlive;

        /// <summary>Why the receive thread ended by itself (a socket error), or null: a stop is not a fault.</summary>
        public string? Fault => m_Fault;

        /// <summary>Datagrams received, valid or not.</summary>
        public long Received => Interlocked.Read(ref m_Received);

        /// <summary>Datagrams that were not a valid pose packet.</summary>
        public long Rejected => Interlocked.Read(ref m_Rejected);

        /// <summary>
        /// Binds and starts the thread. A failed bind (a busy port, a bad
        /// address) returns false and sets <see cref="Error"/> instead of throwing.
        /// </summary>
        public bool Start(IPAddress address, int port)
        {
            Stop();
            Error = null;
            m_Fault = null;
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.ReceiveTimeout = ReceiveTimeoutMs;
                socket.Bind(new IPEndPoint(address, port));
            }
            catch (SocketException e)
            {
                socket.Close();
                Error = $"cannot listen on {address}:{port}: {e.SocketErrorCode} ({e.Message})";
                return false;
            }
            m_Socket = socket;
            Port = ((IPEndPoint)socket.LocalEndPoint).Port;
            m_Stop = false;
            m_Thread = new Thread(() => Run(socket))
            {
                IsBackground = true,
                Name = "HeadTrack UDP",
            };
            m_Thread.Start();
            return true;
        }

        public void Stop()
        {
            m_Stop = true;
            Socket? socket = m_Socket;
            m_Socket = null;
            socket?.Close();
            Thread? thread = m_Thread;
            m_Thread = null;
            if (thread != null && thread != Thread.CurrentThread)
            {
                thread.Join(JoinTimeoutMs);
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void Run(Socket socket)
        {
            var buffer = new byte[2048];
            while (!m_Stop)
            {
                int count;
                try
                {
                    count = socket.Receive(buffer);
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut)
                {
                    continue;
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.MessageSize)
                {
                    // A datagram bigger than the buffer (Windows sockets report
                    // it instead of truncating): not a pose.
                    Interlocked.Increment(ref m_Received);
                    Interlocked.Increment(ref m_Rejected);
                    continue;
                }
                catch (SocketException e)
                {
                    if (!m_Stop)
                    {
                        m_Fault = $"{e.SocketErrorCode} ({e.Message})";
                    }
                    return;
                }
                catch (ObjectDisposedException)
                {
                    if (!m_Stop)
                    {
                        m_Fault = "the socket was closed";
                    }
                    return;
                }
                long now = m_Clock();
                Interlocked.Increment(ref m_Received);
                if (OpenTrackPacket.TryParse(buffer, count, out OpenTrackPose pose))
                {
                    m_Ring.Push(now, pose);
                }
                else
                {
                    Interlocked.Increment(ref m_Rejected);
                }
            }
        }
    }
}
