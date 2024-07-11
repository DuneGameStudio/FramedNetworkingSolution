using System;
using System.Net.Sockets;

namespace Sockets.Interfaces.Session
{
    public interface ITransport : IDisposable
    {
        delegate void OnPacketSent<in SocketAsyncEventArgs>(SocketAsyncEventArgs socketAsyncEventArgs);
        delegate void OnPacketReceived<in SocketAsyncEventArgs, in Guid>(SocketAsyncEventArgs socketAsyncEventArgs, Guid guid);
        void Receive(int bufferSize = 2);
        void Send(ushort packetLength);
    }
}