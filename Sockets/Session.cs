using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Buffers.Binary;
using FramedNetworkingSolution.SocketFramingExtensions.Interfaces;

namespace SocketWrappers
{
    public class Session : ISession
    {
        /// <summary>
        ///     The Session's Main Socket.
        /// </summary>
        private readonly Socket _socket;

        /// <summary>
        /// 
        /// </summary>
        public readonly Guid Id;

        /// <summary>
        ///     Connection Status.
        /// </summary>
        private bool _connected;

        /// <summary>
        /// Disposed State
        /// </summary>
        private bool _disposedValue;

        // /// <summary>
        // ///     Event Arguments For Sending Operation.
        // /// </summary>
        // private readonly SocketAsyncEventArgs _connectEventArgs;

        /// <summary>
        /// Event Arguments For Sending Operation.
        /// </summary>
        private readonly SocketAsyncEventArgs _sendEventArgs;

        /// <summary>
        ///     Event Arguments For Receiving Operation.
        /// </summary>
        private readonly SocketAsyncEventArgs _receiveEventArgs;

        /// <summary>
        ///     Event Arguments For Disconnecting Operation.
        /// </summary>
        private readonly SocketAsyncEventArgs _disconnectEventArgs;

        /// <summary>
        ///     On Packet Sent Event Handler.
        /// </summary>
        public event EventHandler<SocketAsyncEventArgs> OnPacketSentHandler;

        /// <summary>
        ///     On Packet Received Event Handler.
        /// </summary>
        public Action<object, SocketAsyncEventArgs, Guid> OnPacketReceivedHandler;

        /// <summary>
        ///     On Packet Disconnect Event Handler.
        /// </summary>
        public Action<object, SocketAsyncEventArgs, Guid> OnClientDisconnectedHandler;

        /// <summary>
        ///     Initializes The Session Receive Buffer.
        /// </summary>
        public readonly byte[] _sessionReceiveBuffer = new byte[256];

        /// <summary>
        ///     Initializes The Session Send Buffers.
        /// </summary>
        public readonly byte[] _sessionSendBuffer = new byte[256];

        /// <summary>
        ///     Server Session Constructor That Initializes the Socket From An Already Initialized Socket.
        /// </summary>
        /// <param name="socket">The Connected Socket</param>
        public Session(Socket socket, Guid id)//, ServerSocket _Server)
        {
            _socket = socket;

            Id = id;

            // _connectEventArgs = new SocketAsyncEventArgs();
            _sendEventArgs = new SocketAsyncEventArgs();
            _receiveEventArgs = new SocketAsyncEventArgs();
            _disconnectEventArgs = new SocketAsyncEventArgs();

            // OnConnectedHandler += (object sender, SocketAsyncEventArgs onDisconnected) => { };
            OnPacketReceivedHandler += (object sender, SocketAsyncEventArgs onDisconnected, Guid Id) => { };
            OnPacketSentHandler += (object sender, SocketAsyncEventArgs onDisconnected) => { };
            OnClientDisconnectedHandler += (object sender, SocketAsyncEventArgs onDisconnected, Guid Id) => { };

            // _connectEventArgs.Completed += OnTryConnectResponse;
            _sendEventArgs.Completed += OnPacketSent;
            _receiveEventArgs.Completed += OnPacketReceived;
            _disconnectEventArgs.Completed += OnDisconnected;

            _connected = true;
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

                //BitConverter.TryWriteBytes(_sessionSendBuffer.AsSpan(0), packetDataLength);
                BinaryPrimitives.TryWriteUInt16LittleEndian(_sessionSendBuffer.AsSpan(0), packetDataLength);

                packetLength += 2;

                //_sendEventArgs.SetBuffer(_sessionSendBuffer.AsMemory(0, packetLength));
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
        ///     Stop Receiving From Client and Disconnect The Socket None Permanently.
        /// </summary>
        public void Disconnect()
        {
            _connected = false;

            _socket.Shutdown(SocketShutdown.Receive);

            if (!_socket.DisconnectAsync(_disconnectEventArgs))
            {
                OnDisconnected(_socket, _disconnectEventArgs);
            }
        }

        /// <summary>
        ///     On Packet Received Callback.
        /// </summary>
        /// <param name="sender">The Session Socket</param>
        /// <param name="onReceived">Receiving Event Args</param>
        public void OnPacketReceived(object sender, SocketAsyncEventArgs onReceived)
        {
            switch (onReceived.BytesTransferred)
            {
                case 0:
                    Debug.WriteLine("Received And Empty Packet", "log");
                    return;

                case 2:
                    var data = BitConverter.ToUInt16(onReceived.Buffer, 0);
                    Receive(data);
                    return;
            }
            Debug.WriteLine("Received Packet Length: " + onReceived.BytesTransferred, "log");

            OnPacketReceivedHandler.Invoke(sender, onReceived, Id);
        }

        /// <summary>
        ///     On Packet Sent Callback.
        /// </summary>
        /// <param name="sender">This Object's Socket</param>
        /// <param name="onReceived">The SocketAsyncEventArgs That Was Used to Send The Data</param>
        public void OnPacketSent(object sender, SocketAsyncEventArgs onReceived)
        {
            OnPacketSentHandler.Invoke(sender, onReceived);
        }

        /// <summary>
        ///     On Session Socket Disconnect Callback.
        /// </summary>
        public void OnDisconnected(object sender, SocketAsyncEventArgs onDisconnected)
        {
            OnClientDisconnectedHandler.Invoke(sender, onDisconnected, Id);
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
                    _disconnectEventArgs.Dispose();
                }
                _disposedValue = true;
            }
        }

        /// <summary>
        ///     Closes the Server Socket and Disposes it.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}