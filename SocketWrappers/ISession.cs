using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace FramedNetworkingSolution.SocketWrappers
{
    public interface ISession : IDisposable
    {
        event Action<object, SocketAsyncEventArgs, Guid> OnPacketReceivedHandler;
        event EventHandler<SocketAsyncEventArgs> OnPacketSentHandler;
        event Action<object, SocketAsyncEventArgs, Guid> OnClientDisconnectedHandler;

        void Receive(int bufferSize = 2);
        void Send(ushort packetLength);
        void Disconnect();
    }
}