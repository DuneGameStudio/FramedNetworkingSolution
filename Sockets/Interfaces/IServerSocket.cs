using System;
using System.Net.Sockets;

namespace Sockets.SocketFramingExtensions.Interfaces
{
    public interface IServerSocket : IDisposable
    {
        event EventHandler<SocketAsyncEventArgs> OnNewClientConnection;

        event EventHandler<SocketAsyncEventArgs> OnServerDisconnected;

        void Start(string address, int port);

        void Stop();

        void AcceptConnections();

        void StopAcceptingConnections();
    }

}