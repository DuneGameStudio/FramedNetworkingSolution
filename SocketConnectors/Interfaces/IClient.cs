using System;
using System.Net.Sockets;
using Transport.Interfaces;

namespace SocketConnection.Interfaces
{
    public interface IClient : IDisposable
    {
        public event EventHandler<Transport.Interfaces.ITransport> OnConnectedHandler;
        public event EventHandler<SocketAsyncEventArgs> OnDisconnectedHandler;
        public void AttemptConnectAsync(string address, int port);
        public void OnAttemptConnectResponse(object sender, SocketAsyncEventArgs tryConnectEventArgs);
        public void Disconnect();
        public void OnDisconnected(object sender, SocketAsyncEventArgs onDisconnected);
    }
}