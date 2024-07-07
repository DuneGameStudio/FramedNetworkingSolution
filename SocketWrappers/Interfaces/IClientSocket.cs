using System;
using System.Net.Sockets;

namespace FramedNetworkingSolution.SocketWrappers.Interfaces
{
    public interface IClientSocket : IDisposable
    {
        event EventHandler<SocketAsyncEventArgs> OnConnectedHandler;
        event EventHandler<SocketAsyncEventArgs> OnPacketSentHandler;
        delegate void OnPacketReceivedHandler<SocketAsyncEventArgs>(SocketAsyncEventArgs socketAsyncEventArgs);
        delegate void OnClientDisconnectedHandler<SocketAsyncEventArgs>(SocketAsyncEventArgs socketAsyncEventArgs);

        void TryConnect(string address, int port);
        void Receive(int bufferSize = 2);
        void Send(ushort packetLength);
        void Disconnect();
    }
}