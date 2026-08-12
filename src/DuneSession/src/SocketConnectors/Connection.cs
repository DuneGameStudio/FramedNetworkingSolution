using System;
using System.Net.Sockets;
using System.Threading;
using DuneSession.SocketConnectors.Interface;

using DuneTransport.Transport;
using DuneTransport.Transport.Interface;

namespace DuneSession.SocketConnectors
{
    /// <summary>
    /// An active socket connection with an embedded <see cref="ITransport"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wraps a connected socket and provides the <see cref="Transport"/> property for
    /// sending and receiving data. Monitors the transport for <c>SocketDisconnected</c>
    /// and triggers <see cref="OnDisconnected"/> automatically.
    /// </para>
    /// </remarks>
    public class Connection : IConnection
    {
        private readonly Socket socket;
        /// <summary>Connection state flag: 1 = connected, 0 = disconnected.</summary>
        private volatile int connectedState;
        /// <summary>Disconnect in-flight guard: 0 = idle, 1 = disconnecting.</summary>
        private volatile int disconnectingState;

        /// <inheritdoc />
        public bool IsConnected => connectedState == 1 && Transport.IsConnected;

        /// <inheritdoc />
        public ITransport Transport { get; }

        public event Action? OnDisconnected;

        private readonly SocketAsyncEventArgs disconnectAsyncSocketAsyncEventArgs;
        /// <summary>Dispose guard: 0 = active, 1 = disposed.</summary>
        private int _disposed;

        /// <summary>
        /// Creates a new connection wrapping the given socket.
        /// </summary>
        /// <param name="socket">An already-connected socket.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="socket"/> is null.</exception>
        /// <remarks>
        /// <para>
        /// Creates a <see cref="Transport"/> for the socket and subscribes to
        /// <see cref="ITransport.OnPacketReceiveFailed"/> to detect remote disconnects.
        /// </para>
        /// </remarks>
        public Connection(Socket socket)
        {
            this.socket = socket ?? throw new ArgumentNullException(nameof(socket));
            connectedState = 1;

            Transport = new Transport(socket);
            Transport.OnPacketReceiveFailed += OnTransportReceiveFailed;

            disconnectAsyncSocketAsyncEventArgs = new SocketAsyncEventArgs();
            disconnectAsyncSocketAsyncEventArgs.Completed += OnDisconnect;
        }

        /// <inheritdoc />
        public void DisconnectAsync()
        {
            if (Volatile.Read(ref _disposed) == 1)
                return;

            // CR-9: Check connected state after disposed guard to avoid starting
            // async disconnect on an already-disposed connection
            if (connectedState != 1)
                return;

            if (Interlocked.Exchange(ref disconnectingState, 1) != 0)
                return;

            disconnectAsyncSocketAsyncEventArgs.DisconnectReuseSocket = false;

            if (!socket.DisconnectAsync(disconnectAsyncSocketAsyncEventArgs))
            {
                OnDisconnect(this, disconnectAsyncSocketAsyncEventArgs);
            }
        }

        /// <summary>Handles disconnect completion. Transitions to disconnected and fires <see cref="OnDisconnected"/>.</summary>
        private void OnDisconnect(object? sender, SocketAsyncEventArgs e)
        {
            if (Interlocked.Exchange(ref connectedState, 0) != 1)
                return;

            socket.Close();
            try
            {
                OnDisconnected?.Invoke();
            }
            catch { }
        }

        /// <summary>
        /// Handles transport-level receive failures. Auto-disconnects on <c>SocketDisconnected</c>.
        /// </summary>
        private void OnTransportReceiveFailed(ITransport transport, TransportError reason)
        {
            if (reason != TransportError.SocketDisconnected)
                return;

            if (Interlocked.Exchange(ref connectedState, 0) != 1)
                return;

            socket.Close();
            try
            {
                OnDisconnected?.Invoke();
            }
            catch { }
        }

        /// <summary>
        /// Releases all resources held by this connection.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Unsubscribes from transport events, disposes the transport, and closes the socket.
        /// Idempotent: calling multiple times is safe.
        /// </para>
        /// </remarks>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            // CR-9: Mark disconnected BEFORE unsubscribing, so any in-flight
            // OnDisconnect or OnTransportReceiveFailed exits early via the
            // connectedState guard instead of firing OnDisconnected on disposed resources.
            Interlocked.Exchange(ref connectedState, 0);

            disconnectAsyncSocketAsyncEventArgs.Completed -= OnDisconnect;
            Transport.OnPacketReceiveFailed -= OnTransportReceiveFailed;
            disconnectAsyncSocketAsyncEventArgs.Dispose();
            Transport.Dispose();
            socket.Dispose();
        }
    }
}
