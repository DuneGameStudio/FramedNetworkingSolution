using System;
using System.Net.Sockets;

namespace Sockets.Interfaces.Session
{
    public interface ITransport : ITransportConnector, IDisposable
    {
        delegate void OnPacketSent<in SocketAsyncEventArgs>(SocketAsyncEventArgs socketAsyncEventArgs);
        delegate void OnPacketReceived<in SocketAsyncEventArgs, in Guid>(SocketAsyncEventArgs socketAsyncEventArgs, Guid guid);
        void ReceiveAsync(int bufferSize = 2);
        void SendAsync(ushort packetLength);
    }
}