using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace FramedNetworkingSolution.SocketWrappers
{
    public interface IServerSocket : IDisposable
    {
        event EventHandler<SocketAsyncEventArgs> OnNewClientConnection;

        event EventHandler<SocketAsyncEventArgs> OnServerDisconnected;

        void Start(string address, int port);

        void Stop();

        void AcceptConnections();

        void StopAcceptingConnections();
    }

}