using System;
using System.Net.Sockets;

namespace FramedNetworkingSolution.SocketFramingExtensions.Interfaces
{
    public interface ISession : IDisposable
    {
        event EventHandler<SocketAsyncEventArgs> OnPacketSentHandler;
        delegate void OnPacketReceivedHandler<in SocketAsyncEventArgs, in Guid>(SocketAsyncEventArgs socketAsyncEventArgs, Guid guid);
        delegate void OnClientDisconnectedHandler<in SocketAsyncEventArgs, in Guid>(SocketAsyncEventArgs socketAsyncEventArgs, Guid guid);
        void Receive(int bufferSize = 2);
        void Send(ushort packetLength);
        void Disconnect();
    }
}