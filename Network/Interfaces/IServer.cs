using System;
using System.Net.Sockets;
using Sockets.Interfaces.Session;

namespace Network.Interfaces
{
    public interface IServer : IDisposable
    {
        event EventHandler<ITransport> OnNewClientConnection;
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