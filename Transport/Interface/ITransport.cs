using System;
using System.Net.Sockets;
using FramedNetworkingSolution.Utils;

namespace FramedNetworkingSolution.Transport.Interface
{
    public interface ITransport : ITransportConnector, IDisposable
    {
        public Socket socket { get; set; }
        SegmantedBuffer receiveBuffer { get; set; }
        SegmantedBuffer sendBuffer { get; set; }
        event EventHandler<SocketAsyncEventArgs> OnPacketSent;
        event EventHandler<SocketAsyncEventArgs> OnPacketReceived;
        void ReceiveAsync(int bufferSize = 2);
        void SendAsync(Memory<byte> memory);
    }
}