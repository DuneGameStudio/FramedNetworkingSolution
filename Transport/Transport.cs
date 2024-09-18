using System;
using System.Net;
using System.Diagnostics;
using System.Net.Sockets;
using FramedNetworkingSolution.ByteArrayManager;
using FramedNetworkingSolution.Transport.Interface;

namespace FramedNetworkingSolution.Transport
{
    public class Transport : ITransport
    {
        /// <summary>
        ///     The Transport's Socket.
        /// </summary>
        public Socket socket { get; set; }

        /// <summary>
        ///     SegmentedBuffer Instance Used For Receive Operations.
        /// </summary>
        public SegmentedBuffer receiveBuffer { get; set; }

        /// <summary>
        ///     SegmentedBuffer Instance Used For Sending Operations.
        /// </summary>
        public SegmentedBuffer sendBuffer { get; set; }

        /// <summary>
        ///     The Segment Currently Used To Store The Latest Received Packet.
        /// </summary>
        private Segment currentReceiveingSegment;

        /// <summary>
        ///     Event Arguments For Sending Operation.
        /// </summary>
        private readonly SocketAsyncEventArgs sendEventArgs;

        /// <summary>
        ///     Event Arguments For Receiving Operation.
        /// </summary>
        private readonly SocketAsyncEventArgs receiveEventArgs;

        /// <summary>
        ///     Event Arguments For The Connecting Operation.
        /// </summary>
        private readonly SocketAsyncEventArgs connectEventArgs;

        /// <summary>
        ///     Event Arguments For The Disconnecting Operation.
        /// </summary>
        private readonly SocketAsyncEventArgs disconnectEventArgs;

        /// <summary>
        ///     Initializes The Transport Using a Socket.
        ///     This Initializes All SocketAsyncEventArgs and SegmentedBuffer Needed For the Transport Operation.
        /// </summary>
        /// <param name="socket">Socket</param>
        public Transport(Socket socket)
        {
            this.socket = socket;

            sendBuffer = new SegmentedBuffer(8192, 256);
            receiveBuffer = new SegmentedBuffer(8192, 256);

            OnPacketSent += (object sender, SocketAsyncEventArgs onDisconnected) => { };
            OnPacketReceived = (sender, onDisconnected, segment) => { };
            OnAttemptConnectResult += (object sender, SocketAsyncEventArgs onDisconnected) => { };
            OnDisconnected += (object sender, SocketAsyncEventArgs onDisconnected) => { };

            sendEventArgs = new SocketAsyncEventArgs();
            receiveEventArgs = new SocketAsyncEventArgs();
            connectEventArgs = new SocketAsyncEventArgs();
            disconnectEventArgs = new SocketAsyncEventArgs();

            sendEventArgs.Completed += OnPacketSent;
            receiveEventArgs.Completed += PacketReceived;
            connectEventArgs.Completed += OnAttemptConnectResult;
            disconnectEventArgs.Completed += OnDisconnected;
        }

        #region ITransporte
        /// <summary>
        ///     On Packet Sent Event Handler.
        /// </summary>
        // public Action<object, SocketAsyncEventArgs> OnPacketSent;
        public event EventHandler<SocketAsyncEventArgs> OnPacketSent;

        /// <summary>
        ///     On Packet Received Event Handler.
        /// </summary>
        public Action<object, SocketAsyncEventArgs, Segment> OnPacketReceived { get; set; }

        /// <summary>
        ///     Start an Async Receive Operation to receive data from the client using the given buffer size.
        /// </summary>
        /// <param name="bufferSize">The Size of the Buffer to be Allocated for the Receiving of Data From Client.</param>
        public void ReceiveAsync(int bufferSize = 2)
        {
            if (socket.Connected)
            {
                if (receiveBuffer.ReserveMemory(out Segment newSegment, bufferSize))
                {
                    currentReceiveingSegment = newSegment;
                    receiveEventArgs.SetBuffer(newSegment.Memory);

                    if (!socket.ReceiveAsync(receiveEventArgs))
                    {
                        PacketReceived(socket, receiveEventArgs);
                    }

                    return;
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="onReceived"></param>
        private void PacketReceived(object sender, SocketAsyncEventArgs onReceived)
        {
            switch (onReceived.BytesTransferred)
            {
                case 0:
                    Debug.WriteLine("Received And Empty Packet", "log");
                    DisconnectAsync();
                    return;

                case 2:
                    ReceiveAsync(BitConverter.ToUInt16(onReceived.MemoryBuffer.Span));
                    return;
            }

            OnPacketReceived(sender, onReceived, currentReceiveingSegment);
        }

        /// <summary>
        ///     Starts an Async Send Operation to send the provided packet to the client.
        ///     The Function Adds the Length of the Packet To the First 2 bytes of the Packet Before Sending It.
        /// </summary>
        /// <param name="packet">A Memory Of a Byte Array Containing the Data Needed To Be Sent.</param>
        /// <param name="packetLength">The Length of the Packet That Needs to be Sent.</param>
        // public void SendAsync(int packetLength, int index)
        public void SendAsync(Memory<byte> memory)
        {
            if (socket.Connected)
            {
                BitConverter.TryWriteBytes(memory.Span, (ushort)(memory.Length - 2));

                sendEventArgs.SetBuffer(memory);

                if (!socket.SendAsync(sendEventArgs))
                {
                    OnPacketSent(socket, sendEventArgs);
                }
            }
            else
            {
                Debug.WriteLine("Receive | Client Is Not Connected", "error");
            }
        }
        #endregion

        #region ITransportConnector
        /// <summary>
        ///     The IP of the Other Connected End Point 
        /// </summary>
        private IPEndPoint? iPEndPoint;

        /// <summary>
        ///     The TryConnect Result Callback
        /// </summary>
        public event EventHandler<SocketAsyncEventArgs> OnAttemptConnectResult;

        /// <summary>
        ///     The OnDisconnected Callback
        /// </summary>
        public event EventHandler<SocketAsyncEventArgs> OnDisconnected;

        /// <summary>
        ///     Intilizes The Connection Endpoint given the Parameters <paramref name="address"/> and <paramref name="port"/>
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public void Initialize(string address, int port)
        {
            iPEndPoint = new IPEndPoint(IPAddress.Parse(address), port);
        }

        /// <summary>
        ///     Starts and Async Connection Attempt
        /// </summary>
        public void AttemptConnectAsync()
        {
            connectEventArgs.RemoteEndPoint = iPEndPoint;

            if (!socket.ConnectAsync(connectEventArgs))
            {
                OnAttemptConnectResult(socket, connectEventArgs);
            }
        }

        /// <summary>
        ///     Starts an Async Disconnection Operation.
        /// </summary>
        public void DisconnectAsync()
        {
            socket.Shutdown(SocketShutdown.Both);

            if (!socket.DisconnectAsync(disconnectEventArgs))
            {
                OnDisconnected(socket, disconnectEventArgs);
            }
        }
        #endregion

        #region IDisposable
        /// <summary>
        ///     Disposed State
        /// </summary>
        private bool _disposedValue;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Debug.WriteLine("Disposing Transport");
                    // Dispose managed resources
                    socket.Dispose();
                    sendEventArgs.Dispose();
                    receiveEventArgs.Dispose();
                }
                _disposedValue = true;
            }
        }

        /// <summary>
        ///     Disposes the Transport.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}