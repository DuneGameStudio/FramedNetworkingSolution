using System;
using System.Net.Sockets;
using FramedNetworkingSolution.Utils;

namespace FramedNetworkingSolution.Transport.Interface
{
    public interface ITransport : ITransportConnector, IDisposable
    {
        SegmantedBuffer receiveBuffer { get; set; }
        SegmantedBuffer sendBuffer { get; set; }
        event EventHandler<SocketAsyncEventArgs> OnPacketSent;
        event EventHandler<SocketAsyncEventArgs> OnPacketReceived;
        void ReceiveAsync(int bufferSize = 2);
        void SendAsync(int packetLength, int dataLocation);
    }
}