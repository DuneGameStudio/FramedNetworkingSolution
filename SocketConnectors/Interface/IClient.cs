using System;
using System.Net.Sockets;
using FramedNetworkingSolution.Transport.Interface;

namespace FramedNetworkingSolution.SocketConnection.Interface
{
    public interface IClient : IDisposable
    {
        public Action<object, bool, ITransport> OnAttemptConnectResponceHandler { get; }
        public event EventHandler<SocketAsyncEventArgs> OnDisconnectedHandler;
        public void AttemptConnectAsync(string address, int port);
        public void OnAttemptConnectResponse(object sender, SocketAsyncEventArgs tryConnectEventArgs);
        public void Disconnect();
        public void OnDisconnected(object sender, SocketAsyncEventArgs onDisconnected);
    }
}