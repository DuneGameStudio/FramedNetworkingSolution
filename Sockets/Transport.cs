using System;
using System.Diagnostics;
using System.Net.Sockets;
using Sockets.Interfaces.Session;

namespace Sockets
{
    public class Transport : ITransport
    {
        /// <summary>
        ///     The Session's Main Socket.
        /// </summary>
        private readonly Socket _socket;

        /// <summary>
        ///     Connection Status.
        /// </summary>
        private bool _connected;

        /// <summary>
        ///     Disposed State
        /// </summary>
        private bool _disposedValue;

        /// <summary>
        ///     Initializes The Session Receive Buffer.
        /// </summary>
        public readonly byte[] _sessionReceiveBuffer = new byte[256];

        /// <summary>
        ///     Initializes The Session Send Buffers.
        /// </summary>
        public readonly byte[] _sessionSendBuffer = new byte[256];

        /// <summary>
        ///     Event Arguments For Sending Operation.
        /// </summary>
        private readonly SocketAsyncEventArgs _sendEventArgs;

        /// <summary>
        ///     Event Arguments For Receiving Operation.
        /// </summary>
        private readonly SocketAsyncEventArgs _receiveEventArgs;

        /// <summary>
        ///     On Packet Sent Event Handler.
        /// </summary>
        public Action<object, SocketAsyncEventArgs> OnPacketSent;

        /// <summary>
        ///     On Packet Received Event Handler.
        /// </summary>
        public Action<object, SocketAsyncEventArgs> OnPacketReceived;

        public Transport(Socket socket)
        {
            _socket = socket;

            OnPacketSent += (object sender, SocketAsyncEventArgs onDisconnected) => { };
            OnPacketReceived += (object sender, SocketAsyncEventArgs onDisconnected) => { };

            _sendEventArgs = new SocketAsyncEventArgs();
            _receiveEventArgs = new SocketAsyncEventArgs();
        }

        /// <summary>
        ///     Start an Async Receive Operation to receive data from the client using the given buffer size.
        /// </summary>
        /// <param name="bufferSize">The Size of the Buffer to be Allocated for the Receiving of Data From Client.</param>
        public void Receive(int bufferSize = 2)
        {
            if (_connected)
            {
                //_receiveEventArgs.SetBuffer(_sessionReceiveBuffer.AsMemory(0, bufferSize));
                _receiveEventArgs.SetBuffer(_sessionSendBuffer, 0, bufferSize);

                if (!_socket.ReceiveAsync(_receiveEventArgs))
                {
                    OnPacketReceived(_socket, _receiveEventArgs);
                }
            }
            else
            {
                Debug.WriteLine("Receive | Client Is Not Connected", "error");
            }
        }

        /// <summary>
        ///     Starts an Async Send Operation to send the provided packet to the client.
        ///     The Function Adds the Length of the Packet To the Very Start of the Packet Before Sending It.
        /// </summary>
        /// <param name="packet">A Memory Of a Byte Array Containing the Data Needed To Be Sent.</param>
        /// <param name="packetLength">The Length of the Packet That Needs to be Sent.</param>
        public void Send(ushort packetLength) //Span<byte> packet,
        {
            if (_connected)
            {
                // ushort packetDataLength = (ushort)(packetLength - 2);
                ushort packetDataLength = packetLength;

                BitConverter.TryWriteBytes(_sessionSendBuffer.AsSpan(0), packetDataLength);
                // BinaryPrimitives.TryWriteUInt16LittleEndian(_sessionSendBuffer.AsSpan(0), packetDataLength);

                packetLength += 2;

                // _sendEventArgs.SetBuffer(_sessionSendBuffer.AsMemory(0, packetLength));
                _sendEventArgs.SetBuffer(_sessionSendBuffer, 0, packetLength);

                if (!_socket.SendAsync(_sendEventArgs))
                {
                    OnPacketSent(_socket, _sendEventArgs);
                }
            }
            else
            {
                Debug.WriteLine("Receive | Client Is Not Connected", "error");
            }
        }

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
                    // Dispose managed resources
                    _socket.Dispose();
                    _sendEventArgs.Dispose();
                    _receiveEventArgs.Dispose();
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
    }
}