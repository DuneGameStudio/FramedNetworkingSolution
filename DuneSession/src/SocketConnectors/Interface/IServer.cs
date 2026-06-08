using System;
using System.Net.Sockets;

namespace DuneSession.SocketConnectors.Interface
{
    public interface IServerConnector : IDisposable
    {
        bool IsListening { get; }

        event Action<IConnection>? OnClientConnected;
        event Action<SocketError>? OnAcceptFailed;

        void StartListening(string address, int port);
        void AcceptConnection();
        void StopListening();
    }
}
