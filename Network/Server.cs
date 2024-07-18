using System;
using System.Net;
using System.Diagnostics;
using System.Net.Sockets;
using Network.Interfaces;

namespace Network
{
    public class Server : IServer
    {
        /// <summary>
        /// Server Socket.
        /// </summary>
        private Socket _socket;

        /// <summary>
        /// Server Connection Listening State.
        /// </summary>
        private bool _isListening = false;

        /// <summary>
        /// Server New Client Accepting State.
        /// </summary>
        private bool _isAccepting = false;

        /// <summary>
        ///     Wrapper Class For The Event That Fires When a New Client Connects.
        /// </summary>
        private SocketAsyncEventArgs _OnNewClientConnectionEventArgs;

        /// <summary>
        /// Initializes The Server To Accept Connections Asynchronously.
        /// </summary>
        /// <param name="address">Server Address</param>
        /// <param name="port">Address Port</param>
        public Server()
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _OnNewClientConnectionEventArgs = new SocketAsyncEventArgs();

            _OnNewClientConnectionEventArgs.Completed += OnNewConnection;

            OnNewClientConnection = (object sender, SocketAsyncEventArgs onDisconnected) => { };
        }

        #region IServer
        /// <summary>
        /// The Event That Fires When a New Client Connects.
        /// </summary>
        public event EventHandler<SocketAsyncEventArgs> OnNewClientConnection;

        /// <summary>
        /// 
        /// </summary>
        private IPEndPoint? _iPEndPoint;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public void Initialize(string address, int port)
        {
            _iPEndPoint = new IPEndPoint(IPAddress.Parse(address), port);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            _socket.Bind(_iPEndPoint);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public void StartListening()
        {
            if (!_isListening)
            {
                _socket.Listen(-1);
                _isListening = true;
            }
            else
            {
                Debug.WriteLine("StartListenForConnections | Server Already Started.", "Error");
            }
        }

        /// <summary>
        ///     If The Server is Running Listen For Incoming Connections.
        /// </summary>
        public void StopListening()
        {
            if (_isListening)
            {
                _socket.Listen(0);
                _socket.Shutdown(SocketShutdown.Both);
                _isListening = false;
            }
            else
            {
                Debug.WriteLine("StartListenForConnections | Server Has Already Stopped.", "Error");
            }
        }

        /// <summary>
        ///     If The Server is Running Start Accepting Connection Requests Asynchronously Using SocketAsyncEventArgs.
        /// </summary>
        public void AcceptConnection()
        {
            if (_isListening)
            {
                if (!_socket.AcceptAsync(_OnNewClientConnectionEventArgs))
                {
                    OnNewConnection(_socket, _OnNewClientConnectionEventArgs);
                }
            }
            else
            {
                Debug.WriteLine("StartAcceptingConnections | Server is Not Listening.", "Error");
                StopAcceptingConnections();
            }
        }

        public void StartAcceptingConnections()
        {
            _isAccepting = true;
            AcceptConnection();
        }

        /// <summary>
        ///     Stops accepting any new connections
        /// </summary>
        public void StopAcceptingConnections()
        {
            _isAccepting = false;
        }

        /// <summary>
        ///     On New Client Connection Accepted.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="onDisconnected"></param>
        private void OnNewConnection(object sender, SocketAsyncEventArgs onDisconnected)
        {
            OnNewClientConnection(sender, onDisconnected);

            if (_isAccepting)
            {
                AcceptConnection();
            }
        }

        /// <summary>
        ///     Shuts down the server and closes the socket.
        /// </summary>
        public void StopServer()
        {
            _socket.Shutdown(SocketShutdown.Both); // Stops sending and receiving.  
            _socket.Close();
        }

        // /// <summary>
        // /// 
        // /// </summary>
        // public void DisconnectClient()
        // {

        // }
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
                    _OnNewClientConnectionEventArgs.Dispose();
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