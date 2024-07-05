using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace FramedNetworkingSolution.SocketWrappers
{
    public interface IClientSocket : IDisposable
    {
        event EventHandler<SocketAsyncEventArgs> OnConnectedHandler;
        event Action<object, SocketAsyncEventArgs> OnPacketReceivedHandler;
        event EventHandler<SocketAsyncEventArgs> OnPacketSentHandler;
        event Action<object, SocketAsyncEventArgs> OnClientDisconnectedHandler;

        void TryConnect(string address, int port);
        void Receive(int bufferSize = 2);
        void Send(ushort packetLength);
        void Disconnect();
    }

}