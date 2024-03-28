using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace FramedNetworkingSolution.Network.SocketWrappers
{
    public partial class Session : IDisposable
    {
        /// <summary>
        /// The Session's Main Socket.
        /// </summary>
        private readonly Socket _Socket;

        /// <summary>
        /// Connection Status.
        /// </summary>
        private bool _connected;

        /// <summary>
        /// Reconnecting State.
        /// </summary>
        private bool _reconnecting;

        /// <summary>
        /// Event Arguments For Sending Operaion.
        /// </summary>
        private readonly SocketAsyncEventArgs _connectEventArgs;

        /// <summary>
        /// Event Arguments For Sending Operaion.
        /// </summary>
        private readonly SocketAsyncEventArgs _sendEventArgs;

        /// <summary>
        /// Event Arguments For Receiving Operaion.
        /// </summary>
        private readonly SocketAsyncEventArgs _receiveEventArgs;

        /// <summary>
        /// Event Arguments For Disconnecting Operaion.
        /// </summary>
        private readonly SocketAsyncEventArgs _disconnectEventArgs;

        /// <summary>
        /// On Packet Received Event Handler.
        /// </summary>
        public event EventHandler<SocketAsyncEventArgs> OnConnectedHandler;

        /// <summary>
        /// On Packet Received Event Handler.
        /// </summary>
        public event EventHandler<SocketAsyncEventArgs> OnPacketReceivedHandler;

        /// <summary>
        /// On Packet Sent Event Handler.
        /// </summary>
        public event EventHandler<SocketAsyncEventArgs> OnPacketSentHandler;

        /// <summary>
        /// On Packet Disconnect Event Handler.
        /// </summary>
        public event EventHandler<SocketAsyncEventArgs> OnClientDisconnectedHandler;

        /// <summary>
        /// Initializes The Session Receive Buffer.
        /// </summary>
        public readonly byte[] _sessionReceiveBuffer = new byte[256];

        /// <summary>
        /// Initializes The Session Send Buffers.
        /// </summary>
        public readonly byte[] _sessionSendBuffer = new byte[256];

        /// <summary>
        ///     
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public void TryConnect(string address, int port)
        {
            _connectEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Parse(address), port);

            if (!_Socket.ConnectAsync(_connectEventArgs))
            {
                OnTryConnectResponse(_Socket, _connectEventArgs);
            }
        }

        /// <summary>
        /// Attempts An Async Reconnect To The Client.
        /// </summary>
        public void TryReconnectClient()
        {
            _connected = false;
            _reconnecting = true;

            _connectEventArgs.RemoteEndPoint = _Socket.RemoteEndPoint;

            if (!_Socket.ConnectAsync(_connectEventArgs))
            {
                OnTryConnectResponse(_Socket, _connectEventArgs);
            }
        }

        /// <summary>
        /// Start an Async Receive Operation to receive data from the client using the given buffer size.
        /// </summary>
        /// <param name="bufferSize">The Size of the Buffer to be Allocated for the Receiving of Data From Client.</param>
        public void Receive(int bufferSize = 2)
        {
            if (_connected)
            {
                _receiveEventArgs.SetBuffer(_sessionReceiveBuffer.AsMemory(0, bufferSize));

                if (!_Socket.ReceiveAsync(_receiveEventArgs))
                {
                    OnPacketReceived(_Socket, _receiveEventArgs);
                }
            }
            else
            {
                Debug.WriteLine("Receive | Client Is Not Connected", "Error");
            }
        }

        /// <summary>
        /// Starts an Async Send Operation to send the provided packet to the client.
        /// The Function Addes the Length of the Packet To the Very Start of the Packet Before Sending It.
        /// </summary>
        /// <param name="packet">A Memory Of a Byte Array Containing the Data Needed To Be Sent.</param>
        /// <param name="packetlength">The Length of the Packet That Needs to be Sent.</param>
        public void Send(byte[] packet, ushort packetlength)
        {
            if (_connected)
            {
                BitConverter.TryWriteBytes(packet.AsSpan(0), packetlength);

                _sendEventArgs.SetBuffer(packet.AsMemory(0, packetlength));

                if (!_Socket.SendAsync(_sendEventArgs))
                {
                    OnPacketSent(_Socket, _sendEventArgs);
                }
            }
            else
            {
                Debug.WriteLine("Receive | Client Is Not Connected", "Error");
            }
        }

        /// <summary>
        /// Stop Receiving From Client and Disconnect The Socket None Permanently.
        /// </summary>
        public void Disconnect()
        {
            _connected = false;

            _Socket.Shutdown(SocketShutdown.Receive);

            if (!_Socket.DisconnectAsync(_disconnectEventArgs))
            {
                OnDisconnected(_Socket, _disconnectEventArgs);
            }
        }

        /// <summary>
        /// Client Async Reconnection Attempt Callback.
        /// </summary>
        /// <param name="sender">The Settion Socket</param>
        /// <param name="tryConnectEventArgs">Reconnection Event Args</param>
        public void OnTryConnectResponse(object? sender, SocketAsyncEventArgs tryConnectEventArgs)
        {
            if (tryConnectEventArgs.SocketError == SocketError.Success)
            {
                _connected = true;
                _reconnecting = false;

                OnConnectedHandler?.Invoke(sender, tryConnectEventArgs);
            }
            else
            {
                _connected = false;
                _reconnecting = false;

                Debug.WriteLine("Session Try Reconnect Failed", "Log");
            }
        }

        /// <summary>
        /// On Packet Received Callback.
        /// </summary>
        /// <param name="sender">The Settion Socket</param>
        /// <param name="onReceived">Receiving Event Args</param>
        public void OnPacketReceived(object? sender, SocketAsyncEventArgs onReceived)
        {
            switch (onReceived.BytesTransferred)
            {
                case 0:
                    Debug.WriteLine("Received And Empty Packet", "Log");
                    Debug.WriteLine(onReceived.SocketError, "Log");
                    return;

                case 2:
                    var data = BitConverter.ToUInt16(onReceived.MemoryBuffer.Span);
                    Receive(data);
                    return;
            }

            OnPacketReceivedHandler.Invoke(sender, onReceived);
        }

        /// <summary>
        /// On Packet Sent Callback.
        /// </summary>
        /// <param name="sender">This Object's Socket</param>
        /// <param name="onReceived">The SocketAsyncEventArgs That Was Used to Send The Data</param>
        public void OnPacketSent(object? sender, SocketAsyncEventArgs onReceived)
        {
            OnPacketSentHandler.Invoke(sender, onReceived);
        }

        /// <summary>
        /// On Session Socket Disconnect Callback.
        /// </summary>
        public void OnDisconnected(object? sender, SocketAsyncEventArgs onDisconnected)
        {
            OnClientDisconnectedHandler.Invoke(sender, onDisconnected);
        }

        /// <summary>
        /// Closes the Session Socket and Disposes it.
        /// </summary>
        public void Dispose()
        {
            _Socket.Shutdown(SocketShutdown.Both);
            _Socket.Close();
            _Socket.Dispose();
        }
    }
}