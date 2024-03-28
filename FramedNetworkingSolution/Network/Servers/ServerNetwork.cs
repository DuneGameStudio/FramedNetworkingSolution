using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using FramedNetworkingSolution.Network.Servers.Packet;
using FramedNetworkingSolution.Network.SocketWrappers;

namespace FramedNetworkingSolution.Network.Servers;

public class Network
{
    /// <summary>
    /// The Server Network Class.
    /// </summary>
    private readonly ServerSocket _server;

    /// <summary>
    /// Sending State.
    /// </summary>
    private bool _sending;

    /// <summary>
    /// 
    /// </summary>
    private Dictionary<Guid, Session> _clientSessions;

    /// <summary>
    /// 
    /// </summary>
    private ConcurrentQueue<(Guid, IPacket)> _sendQueue;

    /// <summary>
    /// 
    /// </summary>
    public event EventHandler<SocketAsyncEventArgs> OnServerDisconnectedHandler;

    /// <summary>
    /// 
    /// </summary>
    public Action<object?, SocketAsyncEventArgs, Guid> OnNewSessionConnectedHandler;

    /// <summary>
    /// 
    /// </summary>
    public Action<object?, SocketAsyncEventArgs, Guid> OnSessionDisconnectedHandler;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="address"></param>
    /// <param name="port"></param>
    public Network(string address, int port)
    {
        _server = new ServerSocket();
        _server.Start(address, port);

        _clientSessions = new();
        _sendQueue = new();

        _sending = false;

        _server.OnNewClientConnection += OnNewClientConnection;
        _server.OnServerDisconnected += OnServerDisconnected;

        OnServerDisconnectedHandler += (object? clientSessionSocket, SocketAsyncEventArgs ServerDisconnectedEventArgs) => { };
        OnNewSessionConnectedHandler += (object? clientSessionSocket, SocketAsyncEventArgs NewSessionConnectedEventArgs, Guid ID) => { };
        OnSessionDisconnectedHandler += (object? clientSessionSocket, SocketAsyncEventArgs SessionDisconnectedEventArgs, Guid ID) => { };
    }

    /// <summary>
    /// 
    /// </summary>
    public void Start()
    {
        _server.AcceptConnections();
    }

    /// <summary>
    /// 
    /// </summary>
    public void Stop()
    {
        _server.StopAccpetingConnections();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="newClientEventArgs"></param>
    private void OnNewClientConnection(object? sender, SocketAsyncEventArgs newClientEventArgs)
    {
        Socket? clientSocket = newClientEventArgs.AcceptSocket;
        newClientEventArgs.AcceptSocket = null;

        if (clientSocket is not null)
        {
            var session = new Session(clientSocket);

            var ID = Guid.NewGuid();
            _clientSessions.Add(ID, session);

            session.OnClientDisconnectedHandler +=
                (object? clientSessionSocket, SocketAsyncEventArgs clientDisconnectedEventArgs) =>
                    {
                        OnClientDisconnected(clientSessionSocket, clientDisconnectedEventArgs, ID);
                    };

            OnNewSessionConnectedHandler?.Invoke(sender, newClientEventArgs, ID);

            session.Receive();
        }
        _server.AcceptConnections();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="clientSessionSocket"></param>
    /// <param name="clientDisconnectedEventArgs"></param>
    public void OnClientDisconnected(object? clientSessionSocket, SocketAsyncEventArgs clientDisconnectedEventArgs, Guid ID)
    {
        _clientSessions.TryGetValue(ID, out Session? session);

        if (session is not null)
        {
            OnSessionDisconnectedHandler?.Invoke(clientSessionSocket, clientDisconnectedEventArgs, ID);

            _clientSessions.Remove(ID);
            session.Dispose();
        }
        else
        {
            Debug.WriteLine($"Client Disconnected | Session ID: {ID} not found.", "Error");
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="clientSession"></param>
    /// <param name="packet"></param>
    public void EnqueueToSend(Guid clientSessionID, IPacket packet)
        => _sendQueue.Enqueue((clientSessionID, packet));

    /// <summary>
    /// Checks if There's a Queued IPacket in the Send Queue and Serializes it and Sends it.
    /// </summary>
    public void SendQueuedPackets()
    {
        if (_sending == false)
        {
            _sending = true;

            if (_sendQueue.TryDequeue(out (Guid clientSessionID, IPacket packet) sessionPacket))
            {
                _clientSessions.TryGetValue(sessionPacket.clientSessionID, out Session? session);

                session?.Send
                    (
                        sessionPacket.packet.Serialize(session._sessionSendBuffer, out ushort packetLength),
                        packetLength
                    );
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public void DisconnectServer()
    {
        _server.Stop();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="disconnectEventArgs"></param>
    public void OnServerDisconnected(object? sender, SocketAsyncEventArgs disconnectEventArgs)
    {
        OnServerDisconnectedHandler?.Invoke(sender, disconnectEventArgs);
    }
}