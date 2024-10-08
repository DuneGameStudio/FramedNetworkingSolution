using System;
using System.Net;
using System.Diagnostics;
using System.Net.Sockets;
using FramedNetworkingSolution.SocketConnection.Interface;

namespace FramedNetworkingSolution.SocketConnection
{
    public class ClientConnector : IClient
    {
        /// <summary>
        ///     The Client Socket.
        /// </summary>
        private readonly Socket socket;

        /// <summary>
        ///     Socket Connection State This is the Same as the State Inside the Socket Itself.
        /// </summary>
        /// <value></value>
        public bool IsConnected
        {
            get
            {
                return socket.Connected;
            }
        }

        /// <summary>
        ///     Event Arguments For Sending Operations.
        /// </summary>
        private readonly SocketAsyncEventArgs connectEventArgs;

        /// <summary>
        ///     Event Arguments For Disconnecting Operations.
        /// </summary>
        private readonly SocketAsyncEventArgs disconnectEventArgs;

        /// <summary>
        ///     Simple Construction Which Initializes the Socket and The Helper SocketAsyncEventArgs That's Needed.
        /// </summary>
        public ClientConnector()
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            connectEventArgs = new SocketAsyncEventArgs();
            disconnectEventArgs = new SocketAsyncEventArgs();

            OnDisconnectedHandler += (object sender, SocketAsyncEventArgs onDisconnected) => { };
            OnAttemptConnectResponceHandler = (sender, state, connectedTransport) => { };

            connectEventArgs.Completed += OnAttemptConnectResponse;
            disconnectEventArgs.Completed += OnDisconnected;
        }

        #region IClient
        /// <summary>
        ///     On Packet Received Event Handler.
        /// </summary>
        public Action<object, bool, Transport.Interface.ITransport?> OnAttemptConnectResponceHandler { get; }

        /// <summary>
        ///     On Packet Disconnect Event Handler.
        /// </summary>
        public event EventHandler<SocketAsyncEventArgs> OnDisconnectedHandler;

        /// <summary>
        ///     
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public void AttemptConnectAsync(string address, int port)
        {
            connectEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Parse(address), port);

            if (!socket.ConnectAsync(connectEventArgs))
            {
                OnAttemptConnectResponse(socket, connectEventArgs);
            }
        }

        /// <summary>
        ///     Client Async Reconnection Attempt Callback.
        /// </summary>
        /// <param name="sender">The Session Socket</param>
        /// <param name="connectEventArgs">Reconnection Event Args</param>
        public void OnAttemptConnectResponse(object sender, SocketAsyncEventArgs connectEventArgs)
        {
            if (connectEventArgs.SocketError == SocketError.Success)
            {
                OnAttemptConnectResponceHandler(sender, true, new Transport.Transport(connectEventArgs.ConnectSocket));
            }
            else
            {
                OnAttemptConnectResponceHandler(sender, false, null);
                Debug.WriteLine("Session Try Connect Failed", "log");
            }
        }

        /// <summary>
        ///     Stop Receiving From Client and Disconnect The Socket None Permanently.
        /// </summary>
        public void Disconnect()
        {
            socket.Shutdown(SocketShutdown.Both);

            if (!socket.DisconnectAsync(disconnectEventArgs))
            {
                OnDisconnected(socket, disconnectEventArgs);
            }
        }

        /// <summary>
        ///     On Session Socket Disconnect Callback.
        /// </summary>
        public void OnDisconnected(object sender, SocketAsyncEventArgs onDisconnected)
        {
            socket.Close();

            OnDisconnectedHandler(sender, onDisconnected); //, Id);
        }

        #endregion

        #region IDisposable
        /// <summary>
        ///     Disposed State
        /// </summary>
        private bool disposedValue;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // Dispose Managed Resources
                    socket.Dispose();
                    connectEventArgs.Dispose();
                    disconnectEventArgs.Dispose();
                }
                disposedValue = true;
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