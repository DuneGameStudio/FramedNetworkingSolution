using System;
using System.Net;
using System.Diagnostics;
using System.Net.Sockets;
using System.Buffers.Binary;

namespace SocketWrappers
{
    public class ClientSocket
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
        /// Disposed State
        /// </summary>
        private bool _disposedValue;

        /// <summary>
        ///     Event Arguments For Sending Operation.
        /// </summary>
        private readonly SocketAsyncEventArgs _connectEventArgs;

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
        ///     On Packet Received Event Handler.
        /// </summary>
        public event EventHandler<SocketAsyncEventArgs> OnConnectedHandler;

        /// <summary>
        ///     On Packet Received Event Handler.
        /// </summary>
        // public event EventHandler<SocketAsyncEventArgs> OnPacketReceivedHandler;
        public Action<object, SocketAsyncEventArgs> OnPacketReceivedHandler;

        /// <summary>
        ///     On Packet Sent Event Handler.
        /// </summary>
        public event EventHandler<SocketAsyncEventArgs> OnPacketSentHandler;

        /// <summary>
        ///     On Packet Disconnect Event Handler.
        /// </summary>
        public Action<object, SocketAsyncEventArgs> OnClientDisconnectedHandler;

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
        public ClientSocket(Socket socket) //, Guid id)
        {
            _socket = socket;

            // Id = id;

            _connectEventArgs = new SocketAsyncEventArgs();
            _sendEventArgs = new SocketAsyncEventArgs();
            _receiveEventArgs = new SocketAsyncEventArgs();
            _disconnectEventArgs = new SocketAsyncEventArgs();

            OnConnectedHandler += (object sender, SocketAsyncEventArgs onDisconnected) => { };
            OnPacketReceivedHandler += (object sender, SocketAsyncEventArgs onDisconnected) => { };
            OnPacketSentHandler += (object sender, SocketAsyncEventArgs onDisconnected) => { };
            OnClientDisconnectedHandler += (object sender, SocketAsyncEventArgs onDisconnected) => { };

            _connectEventArgs.Completed += OnTryConnectResponse;
            _sendEventArgs.Completed += OnPacketSent;
            _receiveEventArgs.Completed += OnPacketReceived;
            _disconnectEventArgs.Completed += OnDisconnected;

            _connected = true;
        }

        /// <summary>
        ///     
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public void TryConnect(string address, int port)
        {
            _connectEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Parse(address), port);

            if (!_socket.ConnectAsync(_connectEventArgs))
            {
                OnTryConnectResponse(_socket, _connectEventArgs);
            }
        }

        // /// <summary>
        // ///     Attempts An Async Reconnect To The Client.
        // /// </summary>
        // public void TryReconnectClient()
        // {
        //     _connected = false;
        //     _reconnecting = true;

        //     _connectEventArgs.RemoteEndPoint = _socket.RemoteEndPoint;

        //     if (!_socket.ConnectAsync(_connectEventArgs))
        //     {
        //         OnTryConnectResponse(_socket, _connectEventArgs);
        //     }
        // }

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

            _socket.Shutdown(SocketShutdown.Both);

            if (!_socket.DisconnectAsync(_disconnectEventArgs))
            {
                OnDisconnected(_socket, _disconnectEventArgs);
            }
        }

        /// <summary>
        ///     Client Async Reconnection Attempt Callback.
        /// </summary>
        /// <param name="sender">The Session Socket</param>
        /// <param name="tryConnectEventArgs">Reconnection Event Args</param>
        public void OnTryConnectResponse(object sender, SocketAsyncEventArgs tryConnectEventArgs)
        {
            if (tryConnectEventArgs.SocketError == SocketError.Success)
            {
                _connected = true;

                OnConnectedHandler.Invoke(sender, tryConnectEventArgs);
            }
            else
            {
                _connected = false;

                Debug.WriteLine("Session Try Reconnect Failed", "log");
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
                    //var data = BitConverter.ToUInt16(onReceived.MemoryBuffer.Span);
                    var data = BitConverter.ToUInt16(onReceived.Buffer, 0);
                    Receive(data);
                    return;
            }
            Debug.WriteLine("Received Packet Length: " + onReceived.BytesTransferred, "log");

            OnPacketReceivedHandler.Invoke(sender, onReceived); //, Id);
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
            _socket.Close();

            OnClientDisconnectedHandler.Invoke(sender, onDisconnected); //, Id);
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
                    _connectEventArgs.Dispose();
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