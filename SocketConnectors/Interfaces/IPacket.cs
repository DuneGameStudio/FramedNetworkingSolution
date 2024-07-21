using System;

namespace Network.Interfaces
{
    public interface IRequestResponce
    {
        ushort Id { get; set; }

        // public byte[] Serialize(byte[] sendBuffer, out ushort packetLength);

        // public Span<byte> Serialize(Span<byte> sendBuffer, out ushort packetLength);

        ushort Serialize(Span<byte> sendBuffer);

        void Deserialize(byte[] data);
    }
}