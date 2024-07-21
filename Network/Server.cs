using SocketConnection;
using Transport.Interfaces;

namespace Network
{
    public class Server
    {
        ServerConnector serverConnector;

        public Server(string address, int port)
        {
            serverConnector = new ServerConnector();
            serverConnector.Initialize(address, port);
        }

        public void Start()
        {
            serverConnector.StartListening();
            serverConnector.StartAcceptingConnections();
        }

        public void OnNewClientConnection(object sender, ITransport transport)
        {

        }
    }
}