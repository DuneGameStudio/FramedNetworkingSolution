using System;
using DunePresentation.Packet.Interfaces;
using DuneTransport.Transport;

namespace DunePresentation.Peer.Interfaces
{
    public interface IPeer : IDisposable
    {
        event Action? OnDisconnected;

        event Action<TransportError>? OnPacketReceivedHandlerFailed;

        bool IsConnected { get; }

        void StartReceiving();

        bool Send<T>(T packet) where T : IPacket;

        void Disconnect();
    }
}
