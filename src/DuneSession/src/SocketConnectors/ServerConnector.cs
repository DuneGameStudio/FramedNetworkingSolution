using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DuneSession.SocketConnectors.Interface;

namespace DuneSession.SocketConnectors
{
    /// <summary>
    /// Asynchronous TCP server connector implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Manages the listening socket lifecycle. Supports one-shot accept: after each
    /// accepted connection, the application must call <see cref="AcceptConnection"/> again.
    /// </para>
    /// </remarks>
    public class ServerConnector : IServerConnector
    {
        private readonly Socket socket;
        private readonly SocketAsyncEventArgs acceptEventArgs;
        private readonly int _listenBacklog;

        /// <summary>Listening state flag: 0 = stopped, 1 = listening.</summary>
        private volatile int isListening;
        /// <summary>Dispose guard: 0 = active, 1 = disposed.</summary>
        private int _disposed;

        /// <inheritdoc />
        public bool IsListening => isListening == 1;

        public event Action<IConnection>? OnClientConnected;
        public event Action<SocketError>? OnAcceptFailed;

        /// <summary>
        /// Creates a new server connector with the default listen backlog (128).
        /// </summary>
        public ServerConnector() : this(128) { }

        /// <summary>
        /// Creates a new server connector with a custom listen backlog.
        /// </summary>
        /// <param name="listenBacklog">Maximum length of the pending connections queue. Must be > 0.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="listenBacklog"/> is less than 1.</exception>
        public ServerConnector(int listenBacklog)
        {
            if (listenBacklog < 1) throw new ArgumentOutOfRangeException(nameof(listenBacklog));
            _listenBacklog = listenBacklog;

            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            acceptEventArgs = new SocketAsyncEventArgs();
            acceptEventArgs.Completed += OnAcceptCompleted;
        }

        /// <inheritdoc />
        public void StartListening(string address, int port)
        {
            if (Interlocked.Exchange(ref isListening, 1) != 0)
            {
                isListening = 0;
                Debug.WriteLine("StartListening | Server was already listening.", "Error");
                return;
            }

            try
            {
                IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(address), port);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
                socket.Bind(endPoint);
                socket.Listen(_listenBacklog);
            }
            catch
            {
                isListening = 0;
                throw;
            }

            Debug.WriteLine($"Server started listening on {address}:{port}", "log");
        }

        /// <inheritdoc />
        public void StopListening()
        {
            if (Interlocked.Exchange(ref isListening, 0) != 1)
                return;

            try
            {
                socket.Close();
                Debug.WriteLine("Server stopped listening for new connections.", "log");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StopListening | Error during shutdown: {ex.Message}", "Error");
            }
        }

        /// <inheritdoc />
        public void AcceptConnection()
        {
            if (!IsListening)
                return;

            acceptEventArgs.AcceptSocket = null;

            try
            {
                if (!socket.AcceptAsync(acceptEventArgs))
                {
                    ProcessAccept(acceptEventArgs);
                }
            }
            catch (ObjectDisposedException)
            {
                Debug.WriteLine("ObjectDisposedException");
                OnAcceptFailed?.Invoke(SocketError.SocketError);
            }
            catch (SocketException ex)
            {
                Debug.WriteLine($"AcceptConnection | SocketException: {ex.Message}", "Error");
                OnAcceptFailed?.Invoke(ex.SocketErrorCode);
            }
        }

        private void OnAcceptCompleted(object? sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        /// <summary>
        /// Processes a completed accept operation.
        /// </summary>
        /// <remarks>
        /// On success: wraps the accepted socket in a <see cref="Connection"/> and fires <see cref="OnClientConnected"/>.
        /// On failure: fires <see cref="OnAcceptFailed"/> with the socket error code.
        /// </remarks>
        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            if (!IsListening)
                return;

            if (e.SocketError == SocketError.Success && e.AcceptSocket != null)
            {
                IConnection connection = new Connection(e.AcceptSocket);
                OnClientConnected?.Invoke(connection);
            }
            else
            {
                OnAcceptFailed?.Invoke(e.SocketError);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            StopListening();
            acceptEventArgs.Completed -= OnAcceptCompleted;
            acceptEventArgs.Dispose();
        }
    }
}
