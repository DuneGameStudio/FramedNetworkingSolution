using System;

namespace FramedNetworkingSolution.Network.Servers.Packet
{
    public interface IPacket
    {
        ushort Id { get; set; }

        // public byte[] Serialize(byte[] sendBuffer, out ushort packetLength);

        // public Span<byte> Serialize(Span<byte> sendBuffer, out ushort packetLength);

        ushort Serialize(Span<byte> sendBuffer);

        void Deserialize(byte[] data);
    }
}