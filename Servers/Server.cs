using System;
using SocketWrappers;
using System.Diagnostics;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Servers
{
    public class Server : IServer
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
        /// Disposed State
        /// </summary>
        private bool _disposedValue;

        /// <summary>
        /// 
        /// </summary>
        public Dictionary<Guid, Session> ClientSessions;

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
        public event EventHandler<SocketAsyncEventArgs> OnPacketSentHandler;

        /// <summary>
        /// 
        /// </summary>
        public Action<object, SocketAsyncEventArgs, Guid> OnNewSessionConnectedHandler;

        /// <summary>
        /// 
        /// </summary>
        public Action<object, SocketAsyncEventArgs, Guid> OnSessionDisconnectedHandler;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public Server()
        {
            _server = new ServerSocket();

            ClientSessions = new Dictionary<Guid, Session>();
            _sendQueue = new ConcurrentQueue<(Guid, IPacket)>();

            _sending = false;

            _server.OnNewClientConnection += OnNewClientConnection;
            _server.OnServerDisconnected += OnServerDisconnected;

            OnServerDisconnectedHandler += (object clientSessionSocket, SocketAsyncEventArgs ServerDisconnectedEventArgs) => { };
            OnPacketSentHandler += (object clientSessionSocket, SocketAsyncEventArgs SessionDisconnectedEventArgs) => { };

            OnNewSessionConnectedHandler += (object clientSessionSocket, SocketAsyncEventArgs NewSessionConnectedEventArgs, Guid ID) => { };
            OnSessionDisconnectedHandler += (object clientSessionSocket, SocketAsyncEventArgs SessionDisconnectedEventArgs, Guid ID) => { };
        }

        /// <summary>
        /// 
        /// </summary>
        public void Start(string address, int port)
        {
            _server.Start(address, port);

            _server.AcceptConnections();
        }

        /// <summary>
        /// 
        /// </summary>
        public void Stop()
        {
            _server.StopAcceptingConnections();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="newClientEventArgs"></param>
        private void OnNewClientConnection(object sender, SocketAsyncEventArgs newClientEventArgs)
        {
            Socket clientSocket = newClientEventArgs.AcceptSocket;
            newClientEventArgs.AcceptSocket = null;

            if (clientSocket != null)
            {
                Debug.WriteLine("New Client Connected");

                var id = Guid.NewGuid();
                var session = new Session(clientSocket, id);
                ClientSessions.Add(id, session);

                session.OnClientDisconnectedHandler += OnClientDisconnected;
                session.OnPacketSentHandler += OnPacketSent;

                OnNewSessionConnectedHandler.Invoke(clientSocket, newClientEventArgs, id);

                session.Receive();
            }
            _server.AcceptConnections();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="clientSessionSocket"></param>
        /// <param name="clientDisconnectedEventArgs"></param>
        public void OnClientDisconnected(object clientSessionSocket, SocketAsyncEventArgs clientDisconnectedEventArgs, Guid ID)
        {
            ClientSessions.TryGetValue(ID, out Session session);

            if (session != null)
            {
                OnSessionDisconnectedHandler.Invoke(clientSessionSocket, clientDisconnectedEventArgs, ID);

                ClientSessions.Remove(ID);
                session.Dispose();
            }
            else
            {
                Debug.WriteLine($"Client Disconnected | Session ID: {ID} not found.", "error");
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
            if (_sendQueue.IsEmpty) return;

            if (_sending == false)
            {
                _sending = true;
                Debug.WriteLine("Starting Send Queue");
                if (_sendQueue.TryDequeue(out (Guid clientSessionID, IPacket packet) sessionPacket))
                {
                    if (ClientSessions.TryGetValue(sessionPacket.clientSessionID, out Session session))
                    {
                        session.Send
                            (
                                sessionPacket.packet.Serialize(session._sessionSendBuffer.AsSpan(2))
                            );
                        return;
                    }
                }
                _sending = false;
            }
        }

        public void OnPacketSent(object clientSessionSocket, SocketAsyncEventArgs packetSentEventArgs)
        {
            _sending = false;
            Debug.WriteLine("Sent");
            OnPacketSentHandler.Invoke(clientSessionSocket, packetSentEventArgs);
        }

        /// <summary>
        /// 
        /// </summary>
        public void StopServer()
        {
            _server.Stop();
            Dispose();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="disconnectEventArgs"></param>
        public void OnServerDisconnected(object sender, SocketAsyncEventArgs disconnectEventArgs)
        {
            OnServerDisconnectedHandler.Invoke(sender, disconnectEventArgs);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    _server.Dispose();
                    foreach (var session in ClientSessions.Values)
                    {
                        session.Dispose();
                    }
                    ClientSessions.Clear();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}