using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace HeadTrack.Debugging
{
    /// <summary>
    /// The debug channel's socket: text commands from localhost only,
    /// queued by a background thread for the main thread, replies sent back
    /// to the sender's address. Off unless debug_port is set.
    /// </summary>
    internal sealed class DebugChannel : IDisposable
    {
        private readonly ConcurrentQueue<(string Text, EndPoint From)> m_Commands = new ConcurrentQueue<(string, EndPoint)>();
        private Socket? m_Socket;
        private Thread? m_Thread;
        private volatile bool m_Stop;

        public int Port { get; private set; }

        public bool Start(int port, out string? error)
        {
            error = null;
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.ReceiveTimeout = 250;
                socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            }
            catch (SocketException e)
            {
                socket.Close();
                error = $"cannot listen on 127.0.0.1:{port}: {e.SocketErrorCode}";
                return false;
            }
            m_Socket = socket;
            Port = ((IPEndPoint)socket.LocalEndPoint).Port;
            m_Stop = false;
            m_Thread = new Thread(() => Run(socket)) { IsBackground = true, Name = "HeadTrack debug" };
            m_Thread.Start();
            return true;
        }

        public bool TryDequeue(out string text, out EndPoint from)
        {
            if (m_Commands.TryDequeue(out (string Text, EndPoint From) item))
            {
                text = item.Text;
                from = item.From;
                return true;
            }
            text = string.Empty;
            from = new IPEndPoint(IPAddress.Loopback, 0);
            return false;
        }

        public void Reply(EndPoint to, string text)
        {
            Socket? socket = m_Socket;
            if (socket == null)
            {
                return;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length > 60000)
            {
                Array.Resize(ref bytes, 60000);
            }
            try
            {
                socket.SendTo(bytes, to);
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            m_Stop = true;
            m_Socket?.Close();
            m_Socket = null;
            m_Thread?.Join(500);
            m_Thread = null;
        }

        private void Run(Socket socket)
        {
            var buffer = new byte[2048];
            while (!m_Stop)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int count;
                try
                {
                    count = socket.ReceiveFrom(buffer, ref from);
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut || e.SocketErrorCode == SocketError.MessageSize)
                {
                    continue;
                }
                catch (SocketException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                if (from is IPEndPoint ip && IPAddress.IsLoopback(ip.Address))
                {
                    m_Commands.Enqueue((Encoding.UTF8.GetString(buffer, 0, count), from));
                }
            }
        }
    }
}
