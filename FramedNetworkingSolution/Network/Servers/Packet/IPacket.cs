using FramedNetworkingSolution.Network.SocketWrappers;

namespace FramedNetworkingSolution.Network.Servers.Packet;

public interface IPacket
{
    public ushort Id { get; set; }

    public byte[] Serialize(byte[] sendBuffer, out ushort packetlength);

    public void Deserialize(byte[] data);
}