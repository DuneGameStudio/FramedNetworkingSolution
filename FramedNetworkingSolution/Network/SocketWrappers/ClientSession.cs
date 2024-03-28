using System.Net.Sockets;

namespace FramedNetworkingSolution.Network.SocketWrappers
{
    public partial class Session
    {
        /// <summary>
        ///     Client Session Constructor That Initializes the Socket From Scratch.
        /// </summary>
        public Session()
        {
            _Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

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
        }
    }
}