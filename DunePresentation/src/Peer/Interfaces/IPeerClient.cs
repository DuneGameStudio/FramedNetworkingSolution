using System;
using System.Net.Sockets;

namespace DunePresentation.Peer.Interfaces
{
    public interface IPeerClient : IDisposable
    {
        bool IsConnected { get; }

        event Action<IPeer>? OnPeerConnected;
        event Action<SocketError>? OnConnectFailed;

        bool ConnectAsync(string address, int port);
    }
}