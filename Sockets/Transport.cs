using System;
using System.Diagnostics;
using System.Net;
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
        ///     Initializes The Session Receive Buffer.
        /// </summary>
        public readonly byte[] _receiveBuffer = new byte[256];

        /// <summary>
        ///     Initializes The Session Send Buffers.
        /// </summary>
        public readonly byte[] _sendBuffer = new byte[256];

        /// <summary>
        ///     Event Arguments For Sending Operation.
        /// </summary>
        private readonly SocketAsyncEventArgs _sendEventArgs;

        /// <summary>
        ///     Event Arguments For Receiving Operation.
        /// </summary>
        private readonly SocketAsyncEventArgs _receiveEventArgs;

        /// <summary>
        ///     Event Arguments For Sending Operation.
        /// </summary>
        private readonly SocketAsyncEventArgs _connectEventArgs;

        /// <summary>
        ///     Event Arguments For Receiving Operation.
        /// </summary>
        private readonly SocketAsyncEventArgs _disconnectEventArgs;

        public Transport(Socket socket)
        {
            _socket = socket;

            OnPacketSent += (object sender, SocketAsyncEventArgs onDisconnected) => { };
            OnPacketReceived += (object sender, SocketAsyncEventArgs onDisconnected) => { };
            OnTryConnectResult += (object sender, SocketAsyncEventArgs onDisconnected) => { };
            OnDisconnected += (object sender, SocketAsyncEventArgs onDisconnected) => { };

            _sendEventArgs = new SocketAsyncEventArgs();
            _receiveEventArgs = new SocketAsyncEventArgs();
            _connectEventArgs = new SocketAsyncEventArgs();
            _disconnectEventArgs = new SocketAsyncEventArgs();
        }

        #region ITransporte
        /// <summary>
        ///     On Packet Sent Event Handler.
        /// </summary>
        public Action<object, SocketAsyncEventArgs> OnPacketSent;

        /// <summary>
        ///     On Packet Received Event Handler.
        /// </summary>
        public Action<object, SocketAsyncEventArgs> OnPacketReceived;

        /// <summary>
        ///     Start an Async Receive Operation to receive data from the client using the given buffer size.
        /// </summary>
        /// <param name="bufferSize">The Size of the Buffer to be Allocated for the Receiving of Data From Client.</param>
        public void ReceiveAsync(int bufferSize = 2)
        {
            if (_socket.Connected)
            {
                //_receiveEventArgs.SetBuffer(_sessionReceiveBuffer.AsMemory(0, bufferSize));
                _receiveEventArgs.SetBuffer(_sendBuffer, 0, bufferSize);

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
        public void SendAsync(ushort packetLength)
        {
            if (_socket.Connected)
            {
                // ushort packetDataLength = (ushort)(packetLength - 2);
                ushort packetDataLength = packetLength;

                BitConverter.TryWriteBytes(_sendBuffer.AsSpan(0), packetDataLength);
                // BinaryPrimitives.TryWriteUInt16LittleEndian(_sessionSendBuffer.AsSpan(0), packetDataLength);

                packetLength += 2;

                // _sendEventArgs.SetBuffer(_sessionSendBuffer.AsMemory(0, packetLength));
                _sendEventArgs.SetBuffer(_sendBuffer, 0, packetLength);

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
        #endregion

        #region ITransportConnector 
        /// <summary>
        ///     The IP of the Other Connected End Point 
        /// </summary>
        private IPEndPoint? iPEndPoint;

        /// <summary>
        ///     The TryConnect Result Callback
        /// </summary>
        public Action<object, SocketAsyncEventArgs> OnTryConnectResult;

        /// <summary>
        ///     The OnDisconnected Callback
        /// </summary>
        public Action<object, SocketAsyncEventArgs> OnDisconnected;

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
            _connectEventArgs.RemoteEndPoint = iPEndPoint;

            if (!_socket.ConnectAsync(_connectEventArgs))
            {
                OnTryConnectResult(_socket, _connectEventArgs);
            }
        }

        /// <summary>
        ///     Starts an Async Disconnection Operation.
        /// </summary>
        public void DisconnectAsync()
        {
            _socket.Shutdown(SocketShutdown.Both); // Stops sending and receiving.  

            if (!_socket.DisconnectAsync(_disconnectEventArgs))
            {
                OnDisconnected(_socket, _disconnectEventArgs);
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
        #endregion
    }
}