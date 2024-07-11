using System;
using System.Net.Sockets;

namespace Sockets.Interfaces.Session
{
    public interface ITransportConnector
    {
        delegate void OnConnected<in SocketAsyncEventArgs>(SocketAsyncEventArgs socketAsyncEventArgs);
        delegate void OnDisconnected<in SocketAsyncEventArgs>(SocketAsyncEventArgs socketAsyncEventArgs);
        void TryConnect(string address, int port);
        void Disconnect();
    }
}