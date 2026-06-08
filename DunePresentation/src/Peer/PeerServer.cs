using System;
using System.Net.Sockets;
using System.Threading;
using DunePresentation.Encryption.Interface;
using DunePresentation.Packet;
using DunePresentation.Peer.Interfaces;
using DuneSession.SocketConnectors;
using DuneSession.SocketConnectors.Interface;

namespace DunePresentation.Peer
{
    public sealed class PeerServer : IPeerServer
    {
        private readonly PacketRegistry _packetRegistry;
        private readonly Func<IPacketEncryptor>? _encryptorFactory;
        private readonly IServerConnector _serverConnector;

        private int _disposed;

        public bool IsListening => _serverConnector.IsListening;

        public event Action<IPeer>? OnPeerConnected;
        public event Action<SocketError>? OnAcceptFailed;

        public PeerServer(PacketRegistry packetRegistry, Func<IPacketEncryptor>? encryptorFactory = null, IServerConnector? server = null)
        {
            _packetRegistry = packetRegistry ?? throw new ArgumentNullException(nameof(packetRegistry));
            _encryptorFactory = encryptorFactory;

            _serverConnector = server ?? new ServerConnector();
            _serverConnector.OnClientConnected += HandleClientConnected;
            _serverConnector.OnAcceptFailed += HandleAcceptFailed;
        }

        public void StartListening(string address, int port)
            => _serverConnector.StartListening(address, port);

        public void StopListening()
            => _serverConnector.StopListening();

        public void AcceptConnection()
            => _serverConnector.AcceptConnection();

        private void HandleClientConnected(IConnection connection)
        {
            IPacketEncryptor? encryptor = _encryptorFactory?.Invoke();
            var peer = new Peer(connection, _packetRegistry, encryptor);
            OnPeerConnected?.Invoke(peer);
        }

        private void HandleAcceptFailed(SocketError error)
            => OnAcceptFailed?.Invoke(error);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            _serverConnector.OnClientConnected -= HandleClientConnected;
            _serverConnector.OnAcceptFailed -= HandleAcceptFailed;
            _serverConnector.Dispose();
        }
    }
}