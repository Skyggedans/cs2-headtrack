using System;
using System.Net;

namespace HeadTrack.Core
{
    /// <summary>A pose receiver as <see cref="PoseListener"/> drives it; <see cref="UdpPoseReceiver"/> is the real one.</summary>
    public interface IPoseReceiver : IDisposable
    {
        /// <summary>Binds and starts receiving; false (and <see cref="Error"/>) when the address or port cannot be had.</summary>
        bool Start(IPAddress address, int port);

        void Stop();

        bool IsRunning { get; }

        int Port { get; }

        /// <summary>Why the last start failed, or null.</summary>
        string? Error { get; }

        /// <summary>Why a started receiver stopped by itself, or null.</summary>
        string? Fault { get; }
    }

    /// <summary>Where a <see cref="PoseListener"/> stands against the address and port it was last asked for.</summary>
    public enum ListenerState
    {
        /// <summary>Not asked to listen yet, or disposed.</summary>
        Idle,

        /// <summary>Listening where it was asked.</summary>
        Listening,

        /// <summary>The asked-for port could not be had; still listening on the old one, retrying.</summary>
        KeptOld,

        /// <summary>Not listening at all (the bind failed or the receiver died); retrying.</summary>
        Down,
    }

    /// <summary>
    /// Keeps a pose receiver listening where the settings say, called once a
    /// frame from the main thread. A new port is bound before the old one is
    /// let go, so a busy port never leaves the mod deaf; a receive thread
    /// that dies is restarted after a pause that doubles with each failure,
    /// up to <see cref="MaxRetrySeconds"/>, and so is a bind that failed.
    /// <see cref="Status"/> says which, for the log; <see cref="State"/> and
    /// <see cref="ListeningPort"/> say it for the options page.
    /// </summary>
    public sealed class PoseListener : IDisposable
    {
        public const double FirstRetrySeconds = 1.0;

        public const double MaxRetrySeconds = 16.0;

        private readonly Func<IPoseReceiver> m_Create;
        private IPoseReceiver? m_Receiver;
        private IPAddress? m_Address;
        private int m_Port;
        private IPAddress? m_Wanted;
        private int m_WantedPort = -1;
        private double m_NextAttempt = double.NegativeInfinity;
        private double m_Retry = FirstRetrySeconds;
        private double m_RunningSince;

        public PoseListener(Func<IPoseReceiver> create)
        {
            m_Create = create ?? throw new ArgumentNullException(nameof(create));
        }

        public IPoseReceiver? Receiver => m_Receiver;

        public string Status { get; private set; } = "not started";

        /// <summary>Where the receiver stands against the last <see cref="Update"/>'s wish.</summary>
        public ListenerState State
        {
            get
            {
                IPoseReceiver? receiver = m_Receiver;
                if (receiver != null && receiver.IsRunning)
                {
                    return m_Address != null && m_Address.Equals(m_Wanted) && m_Port == m_WantedPort
                        ? ListenerState.Listening
                        : ListenerState.KeptOld;
                }
                return m_Wanted == null ? ListenerState.Idle : ListenerState.Down;
            }
        }

        /// <summary>Something is wrong: not listening where asked (still on the old port, or down).</summary>
        public bool Failing => State == ListenerState.KeptOld || State == ListenerState.Down;

        /// <summary>The port the receiver listens on now, or 0 when none runs.</summary>
        public int ListeningPort
        {
            get
            {
                IPoseReceiver? receiver = m_Receiver;
                return receiver != null && receiver.IsRunning ? receiver.Port : 0;
            }
        }

        /// <summary>
        /// Listens on <paramref name="address"/>:<paramref name="port"/>,
        /// starting, moving or restarting the receiver as needed; returns
        /// true when a new receiver started in this call.
        /// </summary>
        public bool Update(double now, IPAddress address, int port)
        {
            if (!address.Equals(m_Wanted) || port != m_WantedPort)
            {
                // A new wish is tried at once.
                m_Wanted = address;
                m_WantedPort = port;
                m_NextAttempt = double.NegativeInfinity;
                m_Retry = FirstRetrySeconds;
            }
            IPoseReceiver? current = m_Receiver;
            bool running = current != null && current.IsRunning;
            bool there = running && address.Equals(m_Address) && port == m_Port;
            if (there)
            {
                if (now - m_RunningSince >= MaxRetrySeconds)
                {
                    m_Retry = FirstRetrySeconds;
                }
                return false;
            }
            if (current != null && !running && m_NextAttempt == double.NegativeInfinity)
            {
                // It died: say why, and try again after a pause.
                Status = $"receiver on {m_Address}:{m_Port} stopped: {current.Fault ?? "unknown"}; retrying in {m_Retry:0.#} s";
                Schedule(now);
            }
            if (now < m_NextAttempt)
            {
                return false;
            }
            if (current != null && (!running || port == m_Port))
            {
                // A dead receiver, or the same port on another address: let go first.
                current.Dispose();
                m_Receiver = current = null;
            }
            IPoseReceiver next = m_Create();
            if (!next.Start(address, port))
            {
                string why = next.Error ?? $"cannot listen on {address}:{port}";
                next.Dispose();
                Status = current != null
                    ? $"{why}; still listening on {m_Address}:{m_Port}; retrying in {m_Retry:0.#} s"
                    : $"{why}; retrying in {m_Retry:0.#} s";
                Schedule(now);
                return false;
            }
            current?.Dispose();
            m_Receiver = next;
            m_Address = address;
            m_Port = port;
            m_RunningSince = now;
            m_NextAttempt = double.NegativeInfinity;
            Status = $"listening on {address}:{next.Port}";
            return true;
        }

        public void Dispose()
        {
            m_Receiver?.Dispose();
            m_Receiver = null;
            m_Wanted = null;
            m_WantedPort = -1;
            Status = "stopped";
        }

        private void Schedule(double now)
        {
            m_NextAttempt = now + m_Retry;
            m_Retry = Math.Min(MaxRetrySeconds, 2 * m_Retry);
        }
    }
}
