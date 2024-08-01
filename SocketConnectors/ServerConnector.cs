using System;
using FramedNetworkingSolution.Transport;
using System.Net;
using System.Diagnostics;
using System.Net.Sockets;
using FramedNetworkingSolution.SocketConnection.Interface;
using FramedNetworkingSolution.Transport.Interface;

namespace FramedNetworkingSolution.SocketConnection
{
    public class ServerConnector : IServer
    {
        /// <summary>
        /// Server Socket.
        /// </summary>
        private Socket socket;

        /// <summary>
        /// Server Connection Listening State.
        /// </summary>
        private bool isListening = false;

        /// <summary>
        /// Server New Client Accepting State.
        /// </summary>
        private bool isAccepting = false;

        /// <summary>
        ///     Wrapper Class For The Event That Fires When a New Client Connects.
        /// </summary>
        private SocketAsyncEventArgs onNewClientConnectionEventArgs;

        /// <summary>
        ///     Initializes The Server To Accept Connections Asynchronously.
        /// </summary>
        /// <param name="address">Server Address</param>
        /// <param name="port">Address Port</param>
        public ServerConnector()
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            onNewClientConnectionEventArgs = new SocketAsyncEventArgs();

            onNewClientConnectionEventArgs.Completed += OnNewConnection;

            onNewClientConnection = (object sender, ITransport Transport) => { };
        }

        #region IServer
        /// <summary>
        /// The Event That Fires When a New Client Connects.
        /// </summary>
        public event EventHandler<ITransport> onNewClientConnection;

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
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            socket.Bind(_iPEndPoint);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public void StartListening()
        {
            if (!isListening)
            {
                socket.Listen(-1);
                isListening = true;
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
            if (isListening)
            {
                socket.Listen(0);
                socket.Shutdown(SocketShutdown.Both);
                isListening = false;
            }
            else
            {
                Debug.WriteLine("StartListenForConnections | Server Has Already Stopped.", "Error");
            }
        }

        /// <summary>
        ///     If The Server is Running Start Accepting Connection Requests Asynchronously Using SocketAsyncEventArgs.
        /// </summary>
        void AcceptConnection()
        {
            if (isListening)
            {
                if (!socket.AcceptAsync(onNewClientConnectionEventArgs))
                {
                    OnNewConnection(socket, onNewClientConnectionEventArgs);
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
            isAccepting = true;
            AcceptConnection();
        }

        /// <summary>
        ///     Stops accepting any new connections
        /// </summary>
        public void StopAcceptingConnections()
        {
            isAccepting = false;
        }

        /// <summary>
        ///     On New Client Connection Accepted.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="onDisconnected"></param>
        private void OnNewConnection(object sender, SocketAsyncEventArgs onNewClientConnectionEventArgs)
        {
            onNewClientConnection(sender, new Transport.Transport(onNewClientConnectionEventArgs.AcceptSocket));

            onNewClientConnectionEventArgs.AcceptSocket = null;

            if (isAccepting)
            {
                AcceptConnection();
            }
        }

        /// <summary>
        ///     Shuts down the server and closes the socket.
        /// </summary>
        public void StopServer()
        {
            socket.Shutdown(SocketShutdown.Both); // Stops sending and receiving.  
            socket.Close();
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
                    socket.Dispose();
                    onNewClientConnectionEventArgs.Dispose();
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