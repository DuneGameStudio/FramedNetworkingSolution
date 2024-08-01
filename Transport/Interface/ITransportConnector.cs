
using System;
using System.Net.Sockets;

namespace FramedNetworkingSolution.Transport.Interface
{
    public interface ITransportConnector
    {
        event EventHandler<SocketAsyncEventArgs> OnAttemptConnectResult;
        event EventHandler<SocketAsyncEventArgs> OnDisconnected;
        void Initialize(string address, int port);
        void AttemptConnectAsync();
        void DisconnectAsync();
    }
}