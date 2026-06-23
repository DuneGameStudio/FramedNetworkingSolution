using System;
using DunePresentation.Packet;
using DunePresentation.Packet.Interfaces;
using DuneSession.SocketConnectors.Interface;
using DuneTransport.BufferManager;

namespace DunePresentation.Peer.Interfaces
{
    public interface IPeer : IDisposable
    {
        IConnection Connection { get; }

        bool IsConnected { get; }

        event Action<PacketError>? OnSerializeFailed;
        event Action<PacketError>? OnDeserializeFailed;

        Segment SerializeAndEncrypt<T>(T packet) where T : IPacket;

        (IPacket Packet, Action<IPacket> Handler)? DecryptAndDeserialize(Segment segment);

        void Send(Segment segment, int packetSize);

        void DisconnectAsync();
    }
}
