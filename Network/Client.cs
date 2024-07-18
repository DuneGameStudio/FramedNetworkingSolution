using System;
using System.Net;
using System.Diagnostics;
using System.Net.Sockets;
using System.Buffers.Binary;

namespace Network
{
    public class Client
    {
        /// <summary>
        ///     The Session's Main Socket.
        /// </summary>
        private readonly Socket _socket;

        /// <summary>
        ///     Connection Status.
        /// </summary>
        private bool _connected;

        /// <summary>
        ///     Event Arguments For Sending Operation.
        /// </summary>
        private readonly SocketAsyncEventArgs _connectEventArgs;

        /// <summary>
        ///     Event Arguments For Disconnecting Operation.
        /// </summary>
        private readonly SocketAsyncEventArgs _disconnectEventArgs;

        /// <summary>
        ///     On Packet Received Event Handler.
        /// </summary>
        public event EventHandler<SocketAsyncEventArgs> OnConnectedHandler;

        /// <summary>
        ///     On Packet Disconnect Event Handler.
        /// </summary>
        public event EventHandler<SocketAsyncEventArgs> OnDisconnectedHandler;

        /// <summary>
        ///     Server Session Constructor That Initializes the Socket From An Already Initialized Socket.
        /// </summary>
        /// <param name="socket">The Connected Socket</param>
        public Client()
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _connectEventArgs = new SocketAsyncEventArgs();
            _disconnectEventArgs = new SocketAsyncEventArgs();

            OnConnectedHandler += (object sender, SocketAsyncEventArgs onDisconnected) => { };
            OnDisconnectedHandler += (object sender, SocketAsyncEventArgs onDisconnected) => { };

            _connectEventArgs.Completed += OnAttemptConnectResponse;
            _disconnectEventArgs.Completed += OnDisconnected;

            _connected = true;
        }

        #region IClient
        /// <summary>
        ///     
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public void AttemptConnectAsync(string address, int port)
        {
            _connectEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Parse(address), port);

            if (!_socket.ConnectAsync(_connectEventArgs))
            {
                OnAttemptConnectResponse(_socket, _connectEventArgs);
            }
        }

        /// <summary>
        ///     Client Async Reconnection Attempt Callback.
        /// </summary>
        /// <param name="sender">The Session Socket</param>
        /// <param name="tryConnectEventArgs">Reconnection Event Args</param>
        public void OnAttemptConnectResponse(object sender, SocketAsyncEventArgs tryConnectEventArgs)
        {
            if (tryConnectEventArgs.SocketError == SocketError.Success)
            {
                _connected = true;

                OnConnectedHandler(sender, tryConnectEventArgs);
            }
            else
            {
                _connected = false;

                Debug.WriteLine("Session Try Reconnect Failed", "log");
            }
        }

        /// <summary>
        ///     Stop Receiving From Client and Disconnect The Socket None Permanently.
        /// </summary>
        public void Disconnect()
        {
            _connected = false;

            _socket.Shutdown(SocketShutdown.Both);

            if (!_socket.DisconnectAsync(_disconnectEventArgs))
            {
                OnDisconnected(_socket, _disconnectEventArgs);
            }
        }

        /// <summary>
        /// ///     On Session Socket Disconnect Callback.
        /// </summary>
        public void OnDisconnected(object sender, SocketAsyncEventArgs onDisconnected)
        {
            _socket.Close();

            OnDisconnectedHandler(sender, onDisconnected); //, Id);
        }

        #endregion

        #region IDisposable
        /// <summary>
        ///     Disposed State
        /// </summary>
        private bool _disposedValue;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    _socket.Dispose();
                    _connectEventArgs.Dispose();
                    _disconnectEventArgs.Dispose();
                }
                _disposedValue = true;
            }
        }

        /// <summary>
        ///     Closes the Server Socket and Disposes it.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}