using System.Net.Sockets;

namespace FramedNetworkingSolution.Network.SocketWrappers
{
    public partial class Session
    {
        /// <summary>
        ///     Server Session Constructor That Initializes the Socket From An Aleady Intialized Socket.
        /// </summary>
        /// <param name="socket">The Connected Socket</param>
        public Session(Socket socket)//, ServerSocket _Server)
        {
            _Socket = socket;

            _connectEventArgs = new();
            _sendEventArgs = new();
            _receiveEventArgs = new();
            _disconnectEventArgs = new();

            OnConnectedHandler = (object? sender, SocketAsyncEventArgs onDisconnected) => { };
            OnPacketReceivedHandler = (object? sender, SocketAsyncEventArgs onDisconnected) => { };
            OnPacketSentHandler = (object? sender, SocketAsyncEventArgs onDisconnected) => { };
            OnClientDisconnectedHandler = (object? sender, SocketAsyncEventArgs onDisconnected) => { };

            _connectEventArgs.Completed += OnTryConnectResponse;
            _sendEventArgs.Completed += OnPacketSent;
            _receiveEventArgs.Completed += OnPacketReceived;
            _disconnectEventArgs.Completed += OnDisconnected;

            _connected = true;
        }
    }
}