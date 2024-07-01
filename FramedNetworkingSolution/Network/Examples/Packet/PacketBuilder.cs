// using System.Text;

// namespace FramedNetworkingSolution.Network.Servers.Packet;

// public static class PacketBuilder
// {
//     public static int WriteBytes(byte[] sendBuffer, string data, int offset)
//         => Encoding.UTF8.GetBytes(data, 0, data.Length, sendBuffer, offset);

//     public static int WriteBytes(byte[] sendBuffer, ushort data, int offset)
//     {
//         return BitConverter.TryWriteBytes(sendBuffer.AsSpan(offset), data) != false ? 2 : 0;
//     }

//     public static int WriteBytes(byte[] sendBuffer, int data, int offset)
//     {
//         return BitConverter.TryWriteBytes(sendBuffer.AsSpan(offset), data) != false ? 4 : 0;
//     }

//     public static int WriteBytes(byte[] sendBuffer, float data, int offset)
//     {
//         return BitConverter.TryWriteBytes(sendBuffer.AsSpan(offset), data) != false ? 4 : 0;
//     }

//     //

//     public static string GetString(byte[] receiveBuffer, int offset, int stringLength)
//     {
//         return Encoding.UTF8.GetString(receiveBuffer, offset, stringLength);
//     }

//     public static int GetInt(byte[] receiveBuffer, int offset)
//     {
//         return BitConverter.ToInt32(receiveBuffer, offset);
//     }

//     public static ushort GetUShort(byte[] receiveBuffer, int offset)
//     {
//         return BitConverter.ToUInt16(receiveBuffer, offset);
//     }
// }