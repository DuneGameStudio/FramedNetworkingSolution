using System;
using Transport.Interfaces;

namespace SocketConnection.Interfaces
{
    public interface IServer : IDisposable
    {
        event EventHandler<Transport.Interfaces.ITransport> OnNewClientConnection;
        void Initialize(string address, int port);
        void StartListening();
        void StopListening();
        void AcceptConnection();
        void StartAcceptingConnections();
        void StopAcceptingConnections();
        void StopServer();
    }
}