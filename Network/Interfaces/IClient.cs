using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace FramedNetworkingSolution.Network.Interfaces
{
    public interface IClient : IDisposable
    {
        public event EventHandler<SocketAsyncEventArgs> OnConnectedHandler;
        public event EventHandler<SocketAsyncEventArgs> OnDisconnectedHandler;
        public void AttemptConnectAsync(string address, int port);
        public void OnAttemptConnectResponse(object sender, SocketAsyncEventArgs tryConnectEventArgs);
        public void Disconnect();
        public void OnDisconnected(object sender, SocketAsyncEventArgs onDisconnected);
    }
}