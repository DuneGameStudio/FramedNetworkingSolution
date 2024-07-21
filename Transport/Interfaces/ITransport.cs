using System;
using System.Net.Sockets;

namespace Transport.Interfaces
{
    public interface ITransport : ITransportConnector, IDisposable
    {
        delegate void OnPacketSent<in SocketAsyncEventArgs>(SocketAsyncEventArgs socketAsyncEventArgs);
        delegate void OnPacketReceived<in SocketAsyncEventArgs>(SocketAsyncEventArgs socketAsyncEventArgs);
        void ReceiveAsync(int bufferSize = 2);
        void SendAsync(ushort packetLength);
    }
}