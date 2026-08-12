using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DuneSession.SocketConnectors.Interface;


namespace DuneSession.SocketConnectors
{
    /// <summary>
    /// Asynchronous TCP client connector implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Manages the socket lifecycle for outbound connections. Uses an in-flight guard
    /// to prevent simultaneous connection attempts.
    /// </para>
    /// </remarks>
    public class ClientConnector : IClientConnector
    {
        private Socket? socket;
        private volatile IConnection? connection;
        /// <summary>Connection attempt in-flight guard: 0 = idle, 1 = connecting.</summary>
        private volatile int connectingState;
        /// <summary>Dispose guard: 0 = active, 1 = disposed.</summary>
        private int _disposed;

        /// <inheritdoc />
        public bool IsConnected => connection?.IsConnected ?? false;

        public event Action<IConnection>? OnConnected;
        public event Action<SocketError>? OnConnectFailed;

        /// <summary>
        /// Creates a new client connector.
        /// </summary>
        /// <remarks>
        /// Call <see cref="Dispose"/> when no longer needed.
        /// </remarks>
        public ClientConnector() { }

        /// <inheritdoc />
        public bool ConnectAsync(string address, int port)
        {
            // Guard against calls after Dispose — prevents ObjectDisposedException
            if (Volatile.Read(ref _disposed) == 1)
                return false;

            if (IsConnected)
                return false;

            if (Interlocked.Exchange(ref connectingState, 1) != 0)
                return false;

            // Create a fresh SocketAsyncEventArgs for each connection attempt.
            // Reusing a single instance across overlapping async operations can cause
            // callback collisions when the old operation's callback fires after a new
            // operation has already been queued — both callbacks share the same object,
            // and the first one to run zeroes connectingState, causing the other to exit
            // silently and leave the client stuck in Connecting state.
            var connectEventArgs = new SocketAsyncEventArgs();
            connectEventArgs.Completed += OnConnectCompleted;

            try
            {
                // Dispose any leftover socket from a previous failed connect attempt
                socket?.Dispose();
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                connectEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Parse(address), port);

                if (!socket.ConnectAsync(connectEventArgs))
                    ProcessConnect(connectEventArgs);

                return true;
            }
            catch (SocketException)
            {
                connectEventArgs.Dispose();
                socket?.Dispose();
                socket = null;
                Interlocked.Exchange(ref connectingState, 0);
                OnConnectFailed?.Invoke(SocketError.SocketError);
                return false;
            }
            catch (ObjectDisposedException)
            {
                connectEventArgs.Dispose();
                socket?.Dispose();
                socket = null;
                Interlocked.Exchange(ref connectingState, 0);
                OnConnectFailed?.Invoke(SocketError.SocketError);
                return false;
            }
            catch (FormatException)
            {
                connectEventArgs.Dispose();
                socket?.Dispose();
                socket = null;
                Interlocked.Exchange(ref connectingState, 0);
                OnConnectFailed?.Invoke(SocketError.SocketError);
                return false;
            }
        }

        private void OnConnectCompleted(object? sender, SocketAsyncEventArgs e)
        {
            ProcessConnect(e);
        }

        /// <summary>
        /// Processes a completed connection attempt.
        /// </summary>
        /// <remarks>
        /// On success: wraps the socket in a <see cref="Connection"/> and fires <see cref="OnConnected"/>.
        /// On failure: disposes the socket and fires <see cref="OnConnectFailed"/>.
        /// </remarks>
        private void ProcessConnect(SocketAsyncEventArgs e)
        {
            if (Interlocked.Exchange(ref connectingState, 0) != 1)
                return;

            // Dispose the per-attempt SocketAsyncEventArgs to release its pooled buffer
            e.Dispose();

            if (e.SocketError == SocketError.Success)
            {
                connection = new Connection(e.ConnectSocket);
                socket = null; // Connection now owns the socket
                try
                {
                    OnConnected?.Invoke(connection);
                }
                catch
                {
                    connection.Dispose();
                    connection = null;
                }
            }
            else
            {
                socket?.Dispose();
                socket = null;
                try
                {
                    OnConnectFailed?.Invoke(e.SocketError);
                }
                catch { }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            // Cancel any in-flight connect — connectingState guard in ProcessConnect
            // will prevent the callback from processing if we clear the connection
            connection?.Dispose();
            connection = null;
            socket?.Dispose();
            socket = null;
        }
    }
}
