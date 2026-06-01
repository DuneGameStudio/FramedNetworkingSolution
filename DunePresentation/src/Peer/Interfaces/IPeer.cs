using System;
using DunePresentation.Packet.Interfaces;
using DuneTransport.Transport;

namespace DunePresentation.Peer.Interfaces
{
    public interface IPeer : IDisposable
    {
        event Action? OnDisconnected;

        event Action<TransportError>? OnHandlingPacketReceiveFailed;

        event Action<TransportError>? OnPacketReceiveFailed;

        event Action<TransportError>? OnHandlingPacketSendFailed;

        event Action<TransportError>? OnPacketSendFailed;

        bool IsConnected { get; }

        void Receive();

        void Send<T>(T packet) where T : IPacket;

        void Disconnect();
    }
}
