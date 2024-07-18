using System;
using System.Net.Sockets;

namespace Network.Interfaces
{
    public interface IServer : IDisposable
    {
        event EventHandler<SocketAsyncEventArgs> OnNewClientConnection;
        void Initialize(string address, int port);
        void StartListening();
        void StopListening();
        void AcceptConnection();
        void StartAcceptingConnections();
        void StopAcceptingConnections();
        void StopServer();
        // void DisconnectClient();
    }
}