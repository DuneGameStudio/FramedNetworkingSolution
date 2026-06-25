using System;
using System.Diagnostics;
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
        private readonly SocketAsyncEventArgs connectEventArgs;
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
        /// Allocates a <see cref="SocketAsyncEventArgs"/> for connection operations.
        /// Call <see cref="Dispose"/> when no longer needed.
        /// </remarks>
        public ClientConnector()
        {
            connectEventArgs = new SocketAsyncEventArgs();
            connectEventArgs.Completed += OnConnectCompleted;
        }

        /// <inheritdoc />
        public bool ConnectAsync(string address, int port)
        {
            if (IsConnected)
                return false;

            if (Interlocked.Exchange(ref connectingState, 1) != 0)
                return false;

            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                connectEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Parse(address), port);

                if (!socket.ConnectAsync(connectEventArgs))
                    ProcessConnect(connectEventArgs);

                return true;
            }
            catch (Exception e)
            {
                Debug.WriteLine($"ConnectAsync Exception {e}");

                socket?.Dispose();
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

            if (e.SocketError == SocketError.Success)
            {
                connection = new Connection(e.ConnectSocket);
                OnConnected?.Invoke(connection);
            }
            else
            {
                socket?.Dispose();
                OnConnectFailed?.Invoke(e.SocketError);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            connectEventArgs.Completed -= OnConnectCompleted;
            connectEventArgs.Dispose();
            connection?.Dispose();
            socket?.Dispose();
            socket = null;
        }
    }
}
