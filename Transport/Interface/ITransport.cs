using System;
using System.Net.Sockets;
using FramedNetworkingSolution.Utils;

namespace FramedNetworkingSolution.Transport.Interface
{
    public interface ITransport : ITransportConnector, IDisposable
    {
        public Socket socket { get; set; }
        SegmentedBuffer receiveBuffer { get; set; }
        SegmentedBuffer sendBuffer { get; set; }
        event EventHandler<SocketAsyncEventArgs> OnPacketSent;
        Action<object, SocketAsyncEventArgs, Segment> OnPacketReceived { get; set; }

        void ReceiveAsync(int bufferSize = 2);
        void SendAsync(Memory<byte> memory);
    }
}