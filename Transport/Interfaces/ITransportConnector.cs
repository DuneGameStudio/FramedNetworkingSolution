using System;
using System.Net.Sockets;

namespace Transport.Interfaces
{
    public interface ITransportConnector
    {
        delegate void OnTryConnectResult<in SocketAsyncEventArgs>(SocketAsyncEventArgs socketAsyncEventArgs);
        delegate void OnDisconnected<in SocketAsyncEventArgs>(SocketAsyncEventArgs socketAsyncEventArgs);
        void Initialize(string address, int port);
        void AttemptConnectAsync();
        void DisconnectAsync();
    }
}