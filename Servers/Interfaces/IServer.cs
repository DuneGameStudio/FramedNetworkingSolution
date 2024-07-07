using System;
using System.Net.Sockets;

namespace Servers.Interfaces
{
    public interface IServer : IDisposable
    {
        // Events
        event EventHandler<SocketAsyncEventArgs> OnServerDisconnectedHandler;
        event EventHandler<SocketAsyncEventArgs> OnPacketSentHandler;

        // Methods
        void Start(string address, int port);
        void Stop();
        void EnqueueToSend(Guid clientSessionID, IPacket packet);
        void StopServer();
    }

}