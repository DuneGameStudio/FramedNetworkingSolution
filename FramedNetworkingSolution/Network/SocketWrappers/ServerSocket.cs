using System;
using System.Net;
using System.Diagnostics;
using System.Net.Sockets;

namespace FramedNetworkingSolution.Network.SocketWrappers
{
    public class ServerSocket : IDisposable
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
        /// Wrapper Class For The Event That Fires When a New Client Connects.
        /// </summary>
        private SocketAsyncEventArgs _onNewConnectionAcceptedEventArgs;

        /// <summary>
        /// Wrapper Class For The Event That Fires When The Server Disconnects.
        /// </summary>
        private SocketAsyncEventArgs _onDisconnectedEventArgs;

        /// <summary>
        /// The Event That Fires When a New Client Connects.
        /// </summary>
        public event EventHandler<SocketAsyncEventArgs> OnNewClientConnection;

        /// <summary>
        /// The Event That Fires When The Server Disconnects.
        /// </summary>
        public event EventHandler<SocketAsyncEventArgs> OnServerDisconnected;

        /// <summary>
        /// Initializes The Server To Accept Connections Asynchronously.
        /// </summary>
        /// <param name="address">Server Address</param>
        /// <param name="port">Address Port</param>
        public ServerSocket()
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _onNewConnectionAcceptedEventArgs = new SocketAsyncEventArgs();
            _onDisconnectedEventArgs = new SocketAsyncEventArgs();

            _onNewConnectionAcceptedEventArgs.Completed += OnNewConnection;
            _onDisconnectedEventArgs.Completed += OnStopped;

            OnNewClientConnection = (object sender, SocketAsyncEventArgs onDisconnected) => { };
            OnServerDisconnected = (object sender, SocketAsyncEventArgs onDisconnected) => { };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public void Start(string address, int port)
        {
            if (!_isListening)
            {
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
                _socket.Bind(new IPEndPoint(IPAddress.Parse(address), port));
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
        public void Stop()
        {
            if (_isListening)
            {
                _socket.Shutdown(SocketShutdown.Both);

                if (!_socket.DisconnectAsync(_onDisconnectedEventArgs))
                {
                    OnStopped(_socket, _onDisconnectedEventArgs);
                }

                _isListening = true;
            }
            else
            {
                Debug.WriteLine("StartListenForConnections | Server Has Already Stopped.", "Error");
            }
        }

        /// <summary>
        /// If The Server is Running Start Accepting Connection Requests Asynchronously Using SocketAsyncEventArgs.
        /// </summary>
        public void AcceptConnections()
        {
            if (_isListening)
            {
                if (!_socket.AcceptAsync(_onNewConnectionAcceptedEventArgs))
                {
                    OnNewConnection(_socket, _onNewConnectionAcceptedEventArgs);
                }
            }
            else
            {
                Debug.WriteLine("StartAcceptingConnections | Server is Not Listening.", "Error");
            }
        }

        /// <summary>
        ///     Stops accepting any new connections
        /// </summary>
        public void StopAcceptingConnections()
        {
            if (_isListening)
            {
                _isAccepting = false;
            }
            else
            {
                Debug.WriteLine("StopAcceptingConnections | Server is Not Running.", "Error");
            }
        }

        /// <summary>
        ///     On New Client Connection Accepted.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="onDisconnected"></param>
        private void OnNewConnection(object sender, SocketAsyncEventArgs onDisconnected)
        {
            OnNewClientConnection.Invoke(sender, onDisconnected);

            if (_isAccepting)
            {
                AcceptConnections();
            }
        }

        /// <summary>
        /// On Server Disconnection.
        /// </summary>
        /// <param name="sender">The Server Socket Object.</param>
        /// <param name="onStopped">The SocketAsyncEventArgs that's Used To Start the Async Disconnection.</param>
        private void OnStopped(object sender, SocketAsyncEventArgs onStopped)
        {
            _socket.Close();
            _socket.Dispose();

            OnServerDisconnected.Invoke(sender, onStopped);
        }

        /// <summary>
        ///     Closes the Server Socket and Disposes it.
        /// </summary>
        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}