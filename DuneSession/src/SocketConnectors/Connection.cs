using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using DuneSession.SocketConnectors.Interface;
using DuneTransport.Transport;
using DuneTransport.Transport.Interface;

namespace DuneSession.SocketConnectors
{
    public class Connection : IConnection
    {
        private readonly Socket socket;
        private volatile int connectedState;
        private volatile int disconnectingState;

        public bool IsConnected => connectedState == 1 && Transport.IsConnected;
        public ITransport Transport { get; }

        public event Action? OnDisconnected;

        private readonly SocketAsyncEventArgs disconnectAsyncSocketAsyncEventArgs;
        private int _disposed;

        public Connection(Socket socket)
        {
            this.socket = socket ?? throw new ArgumentNullException(nameof(socket));
            connectedState = 1;

            Transport = new Transport(socket);
            Transport.OnPacketReceiveFailed += OnTransportReceiveFailed;

            disconnectAsyncSocketAsyncEventArgs = new SocketAsyncEventArgs();
            disconnectAsyncSocketAsyncEventArgs.Completed += OnDisconnect;
        }

        public void DisconnectAsync()
        {
            if (Volatile.Read(ref _disposed) == 1)
                return;

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

        private void OnDisconnect(object? sender, SocketAsyncEventArgs e)
        {
            if (Interlocked.Exchange(ref connectedState, 0) != 1)
                return;

            socket.Close();
            OnDisconnected?.Invoke();
        }

        private void OnTransportReceiveFailed(ITransport transport, TransportError reason)
        {
            if (reason != TransportError.SocketDisconnected)
                return;

            if (Interlocked.Exchange(ref connectedState, 0) != 1)
                return;

            socket.Close();
            OnDisconnected?.Invoke();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            disconnectAsyncSocketAsyncEventArgs.Completed -= OnDisconnect;
            Transport.OnPacketReceiveFailed -= OnTransportReceiveFailed;
            disconnectAsyncSocketAsyncEventArgs.Dispose();
            Transport.Dispose();
            socket.Dispose();
        }
    }
}
