using System;
using System.Net.Sockets;

namespace DunePresentation.Peer.Interfaces
{
    public interface IPeerServer : IDisposable
    {
        bool IsListening { get; }

        event Action<IPeer>? OnPeerConnected;
        event Action<SocketError>? OnAcceptFailed;

        void StartListening(string address, int port);
        void StopListening();
        void AcceptConnection();
    }
}